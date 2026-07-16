// Native writer for Therion's loch ".lox" 3D model format (BA-B2).
//
// Emits the same chunk layout LoxReader parses (see the format note there): one chunk
// per populated record type, records first, chunk-local data heap after. Used for
// parser round-trip tests and synthetic fixtures; also the seed of a future
// model-export path. Writes the .lox-representable subset of CaveModel — shots must
// reference stations by id (as .lox requires); fields the format cannot store
// (.3d-only metadata, passages, traverse errors) are simply not written.

using System.Buffers.Binary;
using System.Text;

namespace Therion.Blender.Parsing;

/// <summary>Serializes a <see cref="CaveModel"/> to Therion loch <c>.lox</c> bytes.</summary>
public static class LoxWriter
{
    /// <summary>Writes <paramref name="model"/> as a <c>.lox</c> file at <paramref name="path"/>.</summary>
    public static void WriteFile(CaveModel model, string path)
        => File.WriteAllBytes(path, Write(model));

    /// <summary>Serializes <paramref name="model"/> to <c>.lox</c> bytes.</summary>
    public static byte[] Write(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var output = new MemoryStream();

        if (model.Surveys.Count > 0)
            WriteChunk(output, 1, model.Surveys.Count, (records, heap) =>
            {
                foreach (var survey in model.Surveys)
                {
                    U32(records, survey.Id);
                    StringPtr(records, heap, survey.Name);
                    U32(records, survey.ParentId);
                    StringPtr(records, heap, survey.Title);
                }
            });

        if (model.Stations.Count > 0)
            WriteChunk(output, 2, model.Stations.Count, (records, heap) =>
            {
                foreach (var station in model.Stations)
                {
                    U32(records, station.Id);
                    U32(records, station.SurveyId ?? 0);
                    StringPtr(records, heap, station.Name);
                    StringPtr(records, heap, station.Comment);
                    U32(records, station.RawFlags);
                    F64(records, station.Position.X);
                    F64(records, station.Position.Y);
                    F64(records, station.Position.Z);
                }
            });

        if (model.Shots.Count > 0)
            WriteChunk(output, 3, model.Shots.Count, (records, heap) =>
            {
                foreach (var shot in model.Shots)
                {
                    if (shot.FromStationId is not { } from || shot.ToStationId is not { } to)
                        throw new InvalidOperationException(
                            ".lox shots reference stations by id; this shot has none (parsed from a .3d file?).");
                    U32(records, from);
                    U32(records, to);
                    Lrud(records, shot.FromLrud);
                    Lrud(records, shot.ToLrud);
                    U32(records, shot.RawFlags);
                    U32(records, (uint)shot.SectionType);
                    U32(records, shot.SurveyId ?? 0);
                    F64(records, shot.Threshold ?? 60.0);
                }
            });

        if (model.Scraps.Count > 0)
            WriteChunk(output, 4, model.Scraps.Count, (records, heap) =>
            {
                foreach (var scrap in model.Scraps)
                {
                    U32(records, scrap.Id);
                    U32(records, scrap.SurveyId);
                    U32(records, (uint)scrap.Points.Count);
                    BlobPtr(records, heap, (uint)(scrap.Points.Count * 24), () =>
                    {
                        foreach (var point in scrap.Points)
                        {
                            F64(heap, point.X);
                            F64(heap, point.Y);
                            F64(heap, point.Z);
                        }
                    });
                    U32(records, (uint)scrap.Triangles.Count);
                    BlobPtr(records, heap, (uint)(scrap.Triangles.Count * 12), () =>
                    {
                        foreach (var triangle in scrap.Triangles)
                        {
                            U32(heap, triangle.A);
                            U32(heap, triangle.B);
                            U32(heap, triangle.C);
                        }
                    });
                }
            });

        if (model.Surfaces.Count > 0)
            WriteChunk(output, 5, model.Surfaces.Count, (records, heap) =>
            {
                foreach (var surface in model.Surfaces)
                {
                    U32(records, surface.Id);
                    U32(records, surface.Width);
                    U32(records, surface.Height);
                    BlobPtr(records, heap, (uint)(surface.Heights.Count * 8), () =>
                    {
                        foreach (var height in surface.Heights) F64(heap, height);
                    });
                    Calibration(records, surface.Calibration);
                }
            });

        if (model.SurfaceBitmaps.Count > 0)
            WriteChunk(output, 6, model.SurfaceBitmaps.Count, (records, heap) =>
            {
                foreach (var bitmap in model.SurfaceBitmaps)
                {
                    U32(records, bitmap.SurfaceId);
                    U32(records, (uint)bitmap.Type);
                    BlobPtr(records, heap, (uint)bitmap.Data.Length, () => heap.Write(bitmap.Data));
                    Calibration(records, bitmap.Calibration);
                }
            });

        return output.ToArray();
    }

    private static void WriteChunk(MemoryStream output, uint type, int recordCount, Action<MemoryStream, MemoryStream> fill)
    {
        using var records = new MemoryStream();
        using var heap = new MemoryStream();
        fill(records, heap);

        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, type);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)records.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)recordCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)heap.Length);
        output.Write(header);
        records.WriteTo(output);
        heap.WriteTo(output);
    }

    private static void U32(MemoryStream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void F64(MemoryStream stream, double value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Lrud(MemoryStream records, CaveLrud? lrud)
    {
        var value = lrud ?? new CaveLrud(-1, -1, -1, -1);
        F64(records, value.Left);
        F64(records, value.Right);
        F64(records, value.Up);
        F64(records, value.Down);
    }

    private static void Calibration(MemoryStream records, IReadOnlyList<double> calibration)
    {
        if (calibration.Count != 6)
            throw new InvalidOperationException($"Surface calibration must have 6 coefficients, found {calibration.Count}.");
        foreach (var coefficient in calibration) F64(records, coefficient);
    }

    /// <summary>Appends a NUL-terminated UTF-8 string to the heap and writes its
    /// (position, size) reference; null/empty writes the absent reference (0, 0).</summary>
    private static void StringPtr(MemoryStream records, MemoryStream heap, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            U32(records, 0);
            U32(records, 0);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(value);
        U32(records, (uint)heap.Length);
        U32(records, (uint)bytes.Length + 1);
        heap.Write(bytes);
        heap.WriteByte(0);
    }

    /// <summary>Writes a (position, size) reference for a blob about to be appended to
    /// the heap by <paramref name="append"/>.</summary>
    private static void BlobPtr(MemoryStream records, MemoryStream heap, uint size, Action append)
    {
        U32(records, size == 0 ? 0 : (uint)heap.Length);
        U32(records, size);
        long before = heap.Length;
        append();
        if (heap.Length - before != size)
            throw new InvalidOperationException("Blob writer produced a different byte count than declared.");
    }
}
