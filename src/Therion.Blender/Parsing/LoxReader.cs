// Native reader for Therion's loch ".lox" 3D model format (BA-B2).
//
// Format (little-endian throughout): a sequence of chunks until EOF. Each chunk is a
// 16-byte header (type, recSize, recCount, dataSize — u32 each) followed by recSize
// bytes of fixed-layout records and dataSize bytes of chunk-local data heap (strings,
// point/triangle arrays, height grids, image bytes; records reference it by
// position+size pairs). Strings are NUL-terminated UTF-8, the stored size includes the
// NUL, and size <= 1 means "absent". Multiple chunks of the same type are legal and
// accumulate. Chunk types: 1 survey, 2 station, 3 shot, 4 scrap (walls), 5 surface
// grid, 6 surface bitmap.
//
// The format is unpublished; the layout above was established from Therion's own
// reader/writer `lxFile.h`/`lxFile.cxx` and writer `thexpmodel.cxx` (GPL-2.0-or-later,
// used as a normative FORMAT REFERENCE only — no code was copied or transliterated;
// this file is an original implementation, see D-21) and cross-checked against the
// independent MIT-licensed loader in CaveView.js (`src/js/loaders/loxLoader.js`).

using System.Buffers.Binary;
using System.Text;

namespace Therion.Blender.Parsing;

/// <summary>
/// Parses Therion loch <c>.lox</c> files into a <see cref="CaveModel"/>, extracting
/// every field the format carries (D-19). Throws <see cref="CaveFileFormatException"/>
/// on malformed input.
/// </summary>
public static class LoxReader
{
    private const int ChunkHeaderSize = 16;

    private const uint ChunkSurvey = 1;
    private const uint ChunkStation = 2;
    private const uint ChunkShot = 3;
    private const uint ChunkScrap = 4;
    private const uint ChunkSurface = 5;
    private const uint ChunkSurfaceBitmap = 6;

    // Fixed on-disk record sizes, used to bound recCount before allocating.
    private const int SurveyRecordSize = 24;
    private const int StationRecordSize = 52;
    private const int ShotRecordSize = 92;
    private const int ScrapRecordSize = 32;
    private const int SurfaceRecordSize = 68;
    private const int SurfaceBitmapRecordSize = 64;

    /// <summary>Reads and parses the <c>.lox</c> file at <paramref name="path"/>.</summary>
    public static CaveModel ReadFile(string path)
        => Read(File.ReadAllBytes(path), path);

    /// <summary>Parses a <c>.lox</c> file from a stream (fully buffered in memory).</summary>
    public static CaveModel Read(Stream stream, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray(), sourcePath);
    }

    /// <summary>Parses a <c>.lox</c> file from its raw bytes.</summary>
    public static CaveModel Read(byte[] data, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var surveys = new List<CaveSurvey>();
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        var scraps = new List<CaveScrap>();
        var surfaces = new List<CaveSurfaceGrid>();
        var bitmaps = new List<CaveSurfaceBitmap>();

        int pos = 0;
        while (pos < data.Length)
        {
            if (data.Length - pos < ChunkHeaderSize)
                throw Corrupt($"truncated chunk header at offset {pos}");

            uint type = ReadU32(data, pos);
            uint recSize = ReadU32(data, pos + 4);
            uint recCount = ReadU32(data, pos + 8);
            uint dataSize = ReadU32(data, pos + 12);

            long remaining = data.Length - pos - ChunkHeaderSize;
            if (recSize > remaining || dataSize > remaining - recSize)
                throw Corrupt($"chunk at offset {pos} declares {recSize}+{dataSize} bytes but only {remaining} remain");

            var records = data.AsSpan(pos + ChunkHeaderSize, (int)recSize);
            var heap = data.AsSpan(pos + ChunkHeaderSize + (int)recSize, (int)dataSize);

            switch (type)
            {
                case ChunkSurvey:
                    ParseSurveys(records, heap, recCount, surveys);
                    break;
                case ChunkStation:
                    ParseStations(records, heap, recCount, stations);
                    break;
                case ChunkShot:
                    ParseShots(records, recCount, shots);
                    break;
                case ChunkScrap:
                    ParseScraps(records, heap, recCount, scraps);
                    break;
                case ChunkSurface:
                    ParseSurfaces(records, heap, recCount, surfaces);
                    break;
                case ChunkSurfaceBitmap:
                    ParseSurfaceBitmaps(records, heap, recCount, bitmaps);
                    break;
                default:
                    throw Corrupt($"unknown chunk type {type} at offset {pos}");
            }

            pos += ChunkHeaderSize + (int)recSize + (int)dataSize;
        }

        ResolveShotPositions(stations, shots);

        return new CaveModel
        {
            SourcePath = sourcePath,
            SourceFormat = CaveSourceFormat.Lox,
            Surveys = surveys,
            Stations = stations,
            Shots = shots,
            Scraps = scraps,
            Surfaces = surfaces,
            SurfaceBitmaps = bitmaps,
        };
    }

    private static void ParseSurveys(ReadOnlySpan<byte> records, ReadOnlySpan<byte> heap, uint count, List<CaveSurvey> into)
    {
        var cursor = new RecordCursor(records, count, SurveyRecordSize, "survey");
        for (uint i = 0; i < count; i++)
        {
            cursor.BeginRecord();
            uint id = cursor.U32();
            var namePtr = cursor.DataPtr();
            uint parent = cursor.U32();
            var titlePtr = cursor.DataPtr();
            into.Add(new CaveSurvey(id, parent, GetString(heap, namePtr) ?? "", GetString(heap, titlePtr)));
        }
    }

    private static void ParseStations(ReadOnlySpan<byte> records, ReadOnlySpan<byte> heap, uint count, List<CaveStation> into)
    {
        var cursor = new RecordCursor(records, count, StationRecordSize, "station");
        for (uint i = 0; i < count; i++)
        {
            cursor.BeginRecord();
            uint id = cursor.U32();
            uint surveyId = cursor.U32();
            var namePtr = cursor.DataPtr();
            var commentPtr = cursor.DataPtr();
            uint flags = cursor.U32();
            var position = new CaveVector3(cursor.F64(), cursor.F64(), cursor.F64());
            into.Add(new CaveStation
            {
                Id = id,
                SurveyId = surveyId,
                Name = GetString(heap, namePtr) ?? "",
                Comment = GetString(heap, commentPtr),
                Position = position,
                Flags = MapStationFlags(flags),
                RawFlags = flags,
            });
        }
    }

    private static void ParseShots(ReadOnlySpan<byte> records, uint count, List<CaveShot> into)
    {
        var cursor = new RecordCursor(records, count, ShotRecordSize, "shot");
        for (uint i = 0; i < count; i++)
        {
            cursor.BeginRecord();
            uint from = cursor.U32();
            uint to = cursor.U32();
            var fromLrud = new CaveLrud(cursor.F64(), cursor.F64(), cursor.F64(), cursor.F64());
            var toLrud = new CaveLrud(cursor.F64(), cursor.F64(), cursor.F64(), cursor.F64());
            uint flags = cursor.U32();
            uint sectionType = cursor.U32();
            uint surveyId = cursor.U32();
            double threshold = cursor.F64();
            into.Add(new CaveShot
            {
                FromStationId = from,
                ToStationId = to,
                SurveyId = surveyId,
                Flags = MapShotFlags(flags),
                RawFlags = flags,
                SectionType = (CaveShotSection)sectionType,
                FromLrud = fromLrud,
                ToLrud = toLrud,
                Threshold = threshold,
            });
        }
    }

    private static void ParseScraps(ReadOnlySpan<byte> records, ReadOnlySpan<byte> heap, uint count, List<CaveScrap> into)
    {
        var cursor = new RecordCursor(records, count, ScrapRecordSize, "scrap");
        for (uint i = 0; i < count; i++)
        {
            cursor.BeginRecord();
            uint id = cursor.U32();
            uint surveyId = cursor.U32();
            uint numPoints = cursor.U32();
            var pointsPtr = cursor.DataPtr();
            uint numTriangles = cursor.U32();
            var trianglesPtr = cursor.DataPtr();

            var pointBytes = GetBlob(heap, pointsPtr, numPoints, 24, $"scrap {id} points");
            var points = new CaveVector3[numPoints];
            for (int p = 0; p < points.Length; p++)
            {
                points[p] = new CaveVector3(
                    ReadF64(pointBytes, p * 24),
                    ReadF64(pointBytes, p * 24 + 8),
                    ReadF64(pointBytes, p * 24 + 16));
            }

            var triangleBytes = GetBlob(heap, trianglesPtr, numTriangles, 12, $"scrap {id} triangles");
            var triangles = new CaveTriangle[numTriangles];
            for (int t = 0; t < triangles.Length; t++)
            {
                var triangle = new CaveTriangle(
                    ReadU32(triangleBytes, t * 12),
                    ReadU32(triangleBytes, t * 12 + 4),
                    ReadU32(triangleBytes, t * 12 + 8));
                if (triangle.A >= numPoints || triangle.B >= numPoints || triangle.C >= numPoints)
                    throw Corrupt($"scrap {id} triangle {t} references a vertex beyond its {numPoints} points");
                triangles[t] = triangle;
            }

            into.Add(new CaveScrap { Id = id, SurveyId = surveyId, Points = points, Triangles = triangles });
        }
    }

    private static void ParseSurfaces(ReadOnlySpan<byte> records, ReadOnlySpan<byte> heap, uint count, List<CaveSurfaceGrid> into)
    {
        var cursor = new RecordCursor(records, count, SurfaceRecordSize, "surface");
        for (uint i = 0; i < count; i++)
        {
            cursor.BeginRecord();
            uint id = cursor.U32();
            uint width = cursor.U32();
            uint height = cursor.U32();
            var dataPtr = cursor.DataPtr();
            var calibration = ReadCalibration(ref cursor);

            long cells = (long)width * height;
            if (cells > int.MaxValue / 8)
                throw Corrupt($"surface {id} declares an implausible {width}x{height} grid");
            var heightBytes = GetBlob(heap, dataPtr, (uint)cells, 8, $"surface {id} heights");
            var heights = new double[cells];
            for (int h = 0; h < heights.Length; h++)
                heights[h] = ReadF64(heightBytes, h * 8);

            into.Add(new CaveSurfaceGrid
            {
                Id = id,
                Width = width,
                Height = height,
                Calibration = calibration,
                Heights = heights,
            });
        }
    }

    private static void ParseSurfaceBitmaps(ReadOnlySpan<byte> records, ReadOnlySpan<byte> heap, uint count, List<CaveSurfaceBitmap> into)
    {
        var cursor = new RecordCursor(records, count, SurfaceBitmapRecordSize, "surface bitmap");
        for (uint i = 0; i < count; i++)
        {
            cursor.BeginRecord();
            uint surfaceId = cursor.U32();
            uint type = cursor.U32();
            var dataPtr = cursor.DataPtr();
            var calibration = ReadCalibration(ref cursor);

            var imageBytes = GetBlob(heap, dataPtr, dataPtr.Size, 1, $"surface bitmap for surface {surfaceId}");

            into.Add(new CaveSurfaceBitmap
            {
                SurfaceId = surfaceId,
                Type = (CaveBitmapType)type,
                Calibration = calibration,
                Data = imageBytes.ToArray(),
            });
        }
    }

    private static double[] ReadCalibration(ref RecordCursor cursor)
    {
        var calibration = new double[6];
        for (int c = 0; c < calibration.Length; c++)
            calibration[c] = cursor.F64();
        return calibration;
    }

    /// <summary>Fills shot endpoint positions from the station table (unknown station
    /// ids leave the default position — the id itself stays available).</summary>
    private static void ResolveShotPositions(List<CaveStation> stations, List<CaveShot> shots)
    {
        if (shots.Count == 0) return;
        var byId = new Dictionary<uint, CaveVector3>(stations.Count);
        foreach (var station in stations)
            byId[station.Id] = station.Position;
        for (int i = 0; i < shots.Count; i++)
        {
            var shot = shots[i];
            CaveVector3 from = default, to = default;
            if (shot.FromStationId is { } f) byId.TryGetValue(f, out from);
            if (shot.ToStationId is { } t) byId.TryGetValue(t, out to);
            shots[i] = shot with { FromPosition = from, ToPosition = to };
        }
    }

    private static CaveStationFlags MapStationFlags(uint raw)
    {
        var flags = CaveStationFlags.None;
        if ((raw & 1) != 0) flags |= CaveStationFlags.Surface;
        if ((raw & 2) != 0) flags |= CaveStationFlags.Entrance;
        if ((raw & 4) != 0) flags |= CaveStationFlags.Fixed;
        if ((raw & 8) != 0) flags |= CaveStationFlags.Continuation;
        if ((raw & 16) != 0) flags |= CaveStationFlags.HasWalls;
        return flags;
    }

    private static CaveShotFlags MapShotFlags(uint raw)
    {
        var flags = CaveShotFlags.None;
        if ((raw & 1) != 0) flags |= CaveShotFlags.Surface;
        if ((raw & 2) != 0) flags |= CaveShotFlags.Duplicate;
        if ((raw & 4) != 0) flags |= CaveShotFlags.NotVisible;
        if ((raw & 8) != 0) flags |= CaveShotFlags.NotLrud;
        if ((raw & 16) != 0) flags |= CaveShotFlags.Splay;
        return flags;
    }

    /// <summary>A (position, size) reference into a chunk's data heap.</summary>
    internal readonly record struct DataPtr(uint Position, uint Size);

    /// <summary>Bounds-checked sequential reader over a chunk's record bytes.</summary>
    private ref struct RecordCursor
    {
        private readonly ReadOnlySpan<byte> _records;
        private readonly string _what;
        private int _pos;

        public RecordCursor(ReadOnlySpan<byte> records, uint count, int recordSize, string what)
        {
            if ((long)count * recordSize > records.Length)
                throw Corrupt($"{what} chunk declares {count} records but holds only {records.Length} bytes");
            _records = records;
            _what = what;
            _pos = 0;
        }

        public void BeginRecord()
        {
            // Guarded by the constructor check; kept for clarity at call sites.
        }

        public uint U32()
        {
            if (_records.Length - _pos < 4) throw Corrupt($"truncated {_what} record");
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_records[_pos..]);
            _pos += 4;
            return value;
        }

        public double F64()
        {
            if (_records.Length - _pos < 8) throw Corrupt($"truncated {_what} record");
            double value = BinaryPrimitives.ReadDoubleLittleEndian(_records[_pos..]);
            _pos += 8;
            return value;
        }

        public DataPtr DataPtr() => new(U32(), U32());
    }

    /// <summary>Resolves a heap string: NUL-terminated UTF-8, stored size includes the
    /// NUL, size &lt;= 1 means absent (mirrors the reference reader's convention).</summary>
    private static string? GetString(ReadOnlySpan<byte> heap, DataPtr ptr)
    {
        if (ptr.Size <= 1) return null;
        var bytes = GetBlob(heap, ptr, ptr.Size, 1, "string")[..((int)ptr.Size - 1)];
        int nul = bytes.IndexOf((byte)0);
        if (nul >= 0) bytes = bytes[..nul];
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Resolves a heap blob and checks it holds at least
    /// <paramref name="elementCount"/> × <paramref name="elementSize"/> bytes.</summary>
    private static ReadOnlySpan<byte> GetBlob(ReadOnlySpan<byte> heap, DataPtr ptr, uint elementCount, int elementSize, string what)
    {
        if (elementCount == 0) return [];
        if ((long)ptr.Position + ptr.Size > heap.Length)
            throw Corrupt($"{what} reference ({ptr.Position}+{ptr.Size}) escapes its {heap.Length}-byte data heap");
        if ((long)elementCount * elementSize > ptr.Size)
            throw Corrupt($"{what} needs {elementCount}x{elementSize} bytes but the reference holds {ptr.Size}");
        return heap.Slice((int)ptr.Position, (int)ptr.Size);
    }

    private static uint ReadU32(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);

    private static double ReadF64(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadDoubleLittleEndian(span[offset..]);

    private static CaveFileFormatException Corrupt(string detail)
        => new($"Invalid .lox file: {detail}.");
}
