// Generated-input tests for the model readers: random .lox models written and read back exactly, and
// damaged .lox and .3d files (truncated, flipped, with hostile length fields) that must either parse or
// fail with CaveFileFormatException, fast and without allocating from the damaged numbers. Seeds are
// fixed (Therion.TestSupport.GeneratedRuns).

using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using Therion.Blender.Parsing;
using Therion.TestSupport;

namespace Therion.Blender.Tests;

public class ModelFileFuzzTests
{
    private const int SeedBase = 25_000;
    private static readonly TimeSpan TimeBudget = TimeSpan.FromMilliseconds(100);
    private const long AllocationBudget = 64L * 1024 * 1024;

    // ---- generators ----------------------------------------------------------------------------

    private static readonly string[] Names =
    [
        "", "0", "a", "ab", "1.2", "Peștera", "日本語", "e\u0301", "\U0001F600", "a b", "  ", "x\ty", "very-long-" + new string('n', 300),
    ];

    private static string Name(Random random) => Names[random.Next(Names.Length)];
    private static string? Optional(Random random) => random.Next(3) == 0 ? null : NonEmpty(random);
    private static string NonEmpty(Random random) { var n = Name(random); return n.Length == 0 ? "t" : n; }

    private static double Coordinate(Random random) => random.Next(5) switch
    {
        0 => Math.Round(random.NextDouble() * 100 - 50, 3),
        1 => 407_000 + Math.Round(random.NextDouble() * 1000, 2),
        2 => 4_762_000 + Math.Round(random.NextDouble() * 1000, 2),
        3 => -random.NextDouble() * 1e6,
        _ => random.Next(-100, 100),
    };

    private static CaveVector3 Point(Random random) => new(Coordinate(random), Coordinate(random), Coordinate(random));

    private static CaveLrud Lrud(Random random) =>
        random.Next(4) == 0 ? new CaveLrud(-1, -1, -1, -1) : new CaveLrud(random.Next(-1, 6) * 0.5, random.NextDouble() * 4, random.NextDouble() * 3, random.Next(0, 6) * 0.25);

    // The model the reader should return for what the writer writes: shot positions come from the
    // station table (a shot to a station that is not there keeps its id and a default position), the
    // typed flags follow the raw bits, and an empty comment or title is an absent one.
    private static CaveModel GenerateLoxModel(int seed)
    {
        var random = new Random(seed);
        var surveys = new List<CaveSurvey>();
        int surveyCount = random.Next(0, 6);
        for (uint i = 0; i < surveyCount; i++)
            surveys.Add(new CaveSurvey(i, i == 0 ? 0 : (uint)random.Next((int)i), Name(random), Optional(random)));

        var ids = new HashSet<uint>();
        var stations = new List<CaveStation>();
        int stationCount = random.Next(0, 22);
        while (stations.Count < stationCount)
        {
            uint id = random.Next(4) == 0 ? (uint)random.NextInt64(0, uint.MaxValue) : (uint)stations.Count;
            if (!ids.Add(id)) continue;
            uint raw = (uint)random.Next(0, 32);
            stations.Add(new CaveStation
            {
                Id = id, SurveyId = surveyCount == 0 ? 0u : (uint)random.Next(surveyCount), Name = Name(random), Comment = Optional(random),
                Position = Point(random), Flags = StationFlags(raw), RawFlags = raw,
            });
        }
        var position = stations.ToDictionary(s => s.Id, s => s.Position);

        var shots = new List<CaveShot>();
        int shotCount = stations.Count == 0 ? random.Next(0, 3) : random.Next(0, 28);
        for (int i = 0; i < shotCount; i++)
        {
            uint from = stations.Count > 0 && random.Next(10) != 0 ? stations[random.Next(stations.Count)].Id : (uint)random.Next(1000, 2000);
            uint to = stations.Count > 0 && random.Next(10) != 0 ? stations[random.Next(stations.Count)].Id : (uint)random.Next(2000, 3000);
            uint raw = (uint)random.Next(0, 32);
            shots.Add(new CaveShot
            {
                FromStationId = from, ToStationId = to,
                FromPosition = position.GetValueOrDefault(from), ToPosition = position.GetValueOrDefault(to),
                SurveyId = surveyCount == 0 ? 0u : (uint)random.Next(surveyCount),
                Flags = ShotFlags(raw), RawFlags = raw,
                SectionType = (CaveShotSection)random.Next(0, 5),
                FromLrud = Lrud(random), ToLrud = Lrud(random),
                Threshold = random.Next(3) == 0 ? 60.0 : Math.Round(random.NextDouble() * 90, 2),
            });
        }

        var scraps = new List<CaveScrap>();
        for (uint i = 0, n = (uint)random.Next(0, 4); i < n; i++)
        {
            int points = random.Next(0, 12);
            scraps.Add(new CaveScrap
            {
                Id = i, SurveyId = surveyCount == 0 ? 0u : (uint)random.Next(surveyCount),
                Points = Enumerable.Range(0, points).Select(_ => Point(random)).ToArray(),
                Triangles = points == 0 ? [] : Enumerable.Range(0, random.Next(0, 10))
                    .Select(_ => new CaveTriangle((uint)random.Next(points), (uint)random.Next(points), (uint)random.Next(points))).ToArray(),
            });
        }

        var surfaces = new List<CaveSurfaceGrid>();
        for (uint i = 0, n = (uint)random.Next(0, 3); i < n; i++)
        {
            uint w = (uint)random.Next(0, 5), h = (uint)random.Next(0, 5);
            surfaces.Add(new CaveSurfaceGrid
            {
                Id = 10 + i, Width = w, Height = h,
                Calibration = Enumerable.Range(0, 6).Select(_ => Coordinate(random)).ToArray(),
                Heights = Enumerable.Range(0, (int)(w * h)).Select(_ => Coordinate(random)).ToArray(),
            });
        }

        var bitmaps = new List<CaveSurfaceBitmap>();
        foreach (var surface in surfaces.Take(random.Next(0, 3)))
            bitmaps.Add(new CaveSurfaceBitmap
            {
                SurfaceId = surface.Id, Type = (CaveBitmapType)random.Next(0, 2),
                Calibration = Enumerable.Range(0, 6).Select(_ => Coordinate(random)).ToArray(),
                Data = RandomBytes(random, random.Next(0, 40)),
            });

        return new CaveModel
        {
            SourceFormat = CaveSourceFormat.Lox,
            Surveys = surveys, Stations = stations, Shots = shots, Scraps = scraps, Surfaces = surfaces, SurfaceBitmaps = bitmaps,
        };
    }

    private static byte[] RandomBytes(Random random, int count)
    {
        var bytes = new byte[count];
        random.NextBytes(bytes);
        return bytes;
    }

    private static CaveStationFlags StationFlags(uint raw)
    {
        var f = CaveStationFlags.None;
        if ((raw & 1) != 0) f |= CaveStationFlags.Surface;
        if ((raw & 2) != 0) f |= CaveStationFlags.Entrance;
        if ((raw & 4) != 0) f |= CaveStationFlags.Fixed;
        if ((raw & 8) != 0) f |= CaveStationFlags.Continuation;
        if ((raw & 16) != 0) f |= CaveStationFlags.HasWalls;
        return f;
    }

    private static CaveShotFlags ShotFlags(uint raw)
    {
        var f = CaveShotFlags.None;
        if ((raw & 1) != 0) f |= CaveShotFlags.Surface;
        if ((raw & 2) != 0) f |= CaveShotFlags.Duplicate;
        if ((raw & 4) != 0) f |= CaveShotFlags.NotVisible;
        if ((raw & 8) != 0) f |= CaveShotFlags.NotLrud;
        if ((raw & 16) != 0) f |= CaveShotFlags.Splay;
        return f;
    }

    // A well-formed v8 .3d file of random labels, legs, moves, cross-sections, a date and loop-error info.
    // Labels are patched as the format does (delete from the end, append), so every patch is valid.
    private static byte[] GenerateSurvex3d(int seed, out int labels, out int lines)
    {
        var random = new Random(seed);
        var b = Survex3dFixtureBuilder.V8(Name(random).Replace('\0', 'x').Replace('\n', ' ') is { Length: > 0 } t ? t : "t",
            random.Next(3) == 0 ? "EPSG:31700" : null, random.Next(3) == 0 ? '.' : null);
        b.V8Style(0x00); // a 0x00 item stops the file only once the style is known to be Normal
        var label = "";
        labels = lines = 0;
        int items = random.Next(0, 40);
        for (int i = 0; i < items; i++)
        {
            string append = new string(Enumerable.Range(0, random.Next(0, 8)).Select(_ => "abcxyz019.-"[random.Next(11)]).ToArray());
            int delete = random.Next(0, Math.Min(label.Length, 20) + 1);
            var next = label[..^delete] + append;
            double x = Math.Round(Coordinate(random), 2), y = Math.Round(Coordinate(random), 2), z = Math.Round(Coordinate(random) % 1000, 2);
            switch (random.Next(10))
            {
                case 0 or 1 or 2 or 3:
                    b.V8Label(random.Next(0, 0x80 >> 1), delete, append, x, y, z); label = next; labels++; break;
                case 4 or 5 or 6:
                    if (random.Next(2) == 0) b.V8Line(random.Next(0, 8), null, x, y, z);
                    else { b.V8Line(random.Next(0, 8), (delete, append), x, y, z); label = next; }
                    lines++;
                    break;
                case 7: b.V8Move(x, y, z); break;
                case 8:
                    b.V8XSect(delete, append, random.Next(0, 20) * 0.5, random.Next(0, 20) * 0.5, random.Next(0, 20) * 0.5, random.Next(0, 20) * 0.5,
                        last: random.Next(2) == 0, wide: random.Next(2) == 0);
                    label = next;
                    break;
                default: b.V8SingleDate(random.Next(30_000, 50_000)); break;
            }
        }
        return b.V8Stop().Build();
    }

    // ---- exactness -----------------------------------------------------------------------------

    [Fact]
    public void A_written_lox_model_is_read_back_exactly()
    {
        GeneratedRuns.ForEachSeed(SeedBase, 300, seed =>
        {
            var model = GenerateLoxModel(seed);

            var bytes = LoxWriter.Write(model);
            var back = LoxReader.Read(bytes);

            TestCorpus.AssertModelsEqual(model, back);
            // Writing what was read gives the same bytes: the writer is a function of the model alone.
            Assert.Equal(bytes, LoxWriter.Write(back));
        });
    }

    [Fact]
    public void A_generated_survex_file_parses_to_the_legs_and_stations_it_holds()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 10_000, 300, seed =>
        {
            var bytes = GenerateSurvex3d(seed, out var labels, out var lines);

            var model = Survex3dReader.Read(bytes);

            Assert.Equal(CaveSourceFormat.Survex3d, model.SourceFormat);
            Assert.Equal(lines, model.Shots.Count);
            Assert.True(model.Stations.Count >= labels, $"{labels} labelled points but {model.Stations.Count} stations");
            Assert.All(model.Stations, s => Assert.True(double.IsFinite(s.Position.X + s.Position.Y + s.Position.Z)));
        });
    }

    // ---- damaged files -------------------------------------------------------------------------

    private enum Reader { Lox, Survex3d }

    private static CaveModel Parse(Reader reader, byte[] bytes) =>
        reader == Reader.Lox ? LoxReader.Read(bytes) : Survex3dReader.Read(bytes);

    // Runs the reader once: it returns, or throws the documented exception, within the time and
    // allocation budgets. The time is measured twice before it counts as a failure, so a collection
    // pause on a busy machine does not fail a run.
    private static string? Check(Reader reader, byte[] bytes)
    {
        var first = RunOnce(reader, bytes, out var problem);
        if (problem is not null) return problem;
        if (first > TimeBudget && RunOnce(reader, bytes, out problem) > TimeBudget)
            return $"took {first.TotalMilliseconds:0} ms for {bytes.Length} bytes";
        return problem;
    }

    private static TimeSpan RunOnce(Reader reader, byte[] bytes, out string? problem)
    {
        problem = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        try { Parse(reader, bytes); }
        catch (CaveFileFormatException) { /* the documented failure */ }
        catch (Exception ex) { problem = $"{ex.GetType().Name}: {ex.Message}"; }
        clock.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (problem is null && allocated > AllocationBudget)
            problem = $"allocated {allocated / (1024 * 1024)} MB for {bytes.Length} bytes";
        return clock.Elapsed;
    }

    private static readonly uint[] HostileValues =
        [0xFFFFFFFF, 0x80000000, 0x7FFFFFFF, 0x7FFFFFF0, 0xFFFFFFFE, 0x00010000, 0x01000000, 0x00000000, 0x00000001, 0xC0000000];

    // One damaged copy of the file: a cut, some flipped bytes, a hostile 32-bit field, or a stretch of garbage.
    private static byte[] Damage(Random random, byte[] original, bool alignFields)
    {
        var bytes = (byte[])original.Clone();
        if (bytes.Length == 0) return bytes;
        switch (random.Next(5))
        {
            case 0:
                return bytes[..random.Next(bytes.Length)];
            case 1:
                for (int i = 0, n = random.Next(1, 9); i < n; i++) bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                return bytes;
            case 2:
                for (int i = 0, n = random.Next(1, 5); i < n; i++) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                return bytes;
            case 3:
            {
                // A 32-bit field set to a hostile number: 4-aligned half of the time, which hits the
                // chunk headers and record fields of a .lox file squarely.
                if (bytes.Length < 4) return bytes;
                int at = random.Next(bytes.Length - 3);
                if (alignFields) at &= ~3;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), HostileValues[random.Next(HostileValues.Length)]);
                return bytes;
            }
            default:
            {
                int at = random.Next(bytes.Length), len = Math.Min(random.Next(1, 40), bytes.Length - at);
                random.NextBytes(bytes.AsSpan(at, len));
                return bytes;
            }
        }
    }

    [Fact]
    public void A_damaged_lox_file_parses_or_fails_cleanly_and_quickly()
    {
        Parse(Reader.Lox, LoxWriter.Write(GenerateLoxModel(SeedBase))); // warm the reader up before timing
        GeneratedRuns.ForEachSeed(SeedBase + 20_000, 2500, seed =>
        {
            var random = new Random(seed);
            var original = LoxWriter.Write(GenerateLoxModel(seed % 400));
            var damaged = Damage(random, original, alignFields: true);

            var problem = Check(Reader.Lox, damaged);

            Assert.True(problem is null, $"{problem} (model seed {seed % 400}, {damaged.Length} bytes)");
        });
    }

    [Fact]
    public void A_damaged_survex_file_parses_or_fails_cleanly_and_quickly()
    {
        Parse(Reader.Survex3d, GenerateSurvex3d(SeedBase, out _, out _));
        GeneratedRuns.ForEachSeed(SeedBase + 30_000, 2500, seed =>
        {
            var random = new Random(seed);
            var original = GenerateSurvex3d(seed % 400, out _, out _);
            var damaged = Damage(random, original, alignFields: false);

            var problem = Check(Reader.Survex3d, damaged);

            Assert.True(problem is null, $"{problem} (file seed {seed % 400}, {damaged.Length} bytes)");
        });
    }

    [Fact]
    public void The_two_headers_with_hostile_sizes_are_refused_without_allocating()
    {
        // Every chunk type with a record count, a record size or a data size at the extremes.
        foreach (uint type in new uint[] { 0, 1, 2, 3, 4, 5, 6, 7, 99, 0xFFFFFFFF })
            foreach (var recSize in HostileValues)
                foreach (var recCount in HostileValues)
                {
                    var bytes = new byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, type);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), recSize);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), recCount);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), recSize);
                    var problem = Check(Reader.Lox, bytes);
                    Assert.True(problem is null, $"type {type}, size {recSize}, count {recCount}: {problem}");
                }
    }

    [Fact]
    public void Random_bytes_after_a_valid_header_parse_or_fail_cleanly()
    {
        GeneratedRuns.ForEachSeed(SeedBase + 40_000, 600, seed =>
        {
            var random = new Random(seed);
            var tail = RandomBytes(random, random.Next(0, 300));
            var v8 = Survex3dFixtureBuilder.V8("t").Build().Concat(tail).ToArray();
            var lox = RandomBytes(random, random.Next(0, 300));

            Assert.Null(Check(Reader.Survex3d, v8));
            Assert.Null(Check(Reader.Lox, lox));
        });
    }
}
