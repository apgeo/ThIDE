// Invented caves for the reveal tests: the invented caves of the scene probe with survey dates
// laid on their legs, the same caves written as a Survex .3d file (which is where survey dates
// travel), and a few caves of three or four legs whose reveal values can be worked out by hand.
// Nothing here is surveyed data.

using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D;

namespace Therion.Blender.Tests;

internal static partial class RevealFixtures
{
    private static readonly DateOnly Day1900 = new(1900, 1, 1);

    /// <summary>The model with other legs.</summary>
    public static CaveModel WithShots(CaveModel model, IReadOnlyList<CaveShot> shots, CaveSourceFormat? format = null) => new()
    {
        SourcePath = model.SourcePath,
        SourceFormat = format ?? model.SourceFormat,
        FormatVersion = model.FormatVersion,
        Title = model.Title,
        CoordinateSystem = model.CoordinateSystem,
        SeparatorChar = model.SeparatorChar,
        Datestamp = model.Datestamp,
        Timestamp = model.Timestamp,
        IsExtendedElevation = model.IsExtendedElevation,
        Surveys = model.Surveys,
        Stations = model.Stations,
        Shots = shots,
        Scraps = model.Scraps,
        Surfaces = model.Surfaces,
        SurfaceBitmaps = model.SurfaceBitmaps,
        Passages = model.Passages,
        UnknownChunkTypes = model.UnknownChunkTypes,
        TraverseErrors = model.TraverseErrors,
    };

    /// <summary>The model with the day <paramref name="dayOf"/> gives each leg (by the leg and
    /// its place among the model's shots); null leaves a leg without one.</summary>
    public static CaveModel Dated(CaveModel model, Func<CaveShot, int, DateOnly?> dayOf)
        => WithShots(model, model.Shots.Select((shot, i) => dayOf(shot, i) is { } day ? shot with { DateFrom = day, DateTo = day } : shot).ToList());

    /// <summary>The medium invented cave as three expeditions: the upper level in 1987, the
    /// pitch in 1994, the lower level in 2003, each on a day of its own.</summary>
    public static readonly DateOnly UpperDay = new(1987, 7, 12), PitchDay = new(1994, 3, 1), LowerDay = new(2003, 10, 20);

    public static CaveModel MediumByExpedition()
    {
        var model = InventedCave.Build(InventedCaveSize.Medium);
        var names = model.Surveys.ToDictionary(s => s.Id, s => s.Name);
        return Dated(model, (shot, _) => names[shot.SurveyId ?? 0] switch
        {
            "upper" => UpperDay,
            "pitch" => PitchDay,
            _ => LowerDay,
        });
    }

    /// <summary>
    /// The model as the bytes of a Survex <c>.3d</c> file (version 8): its legs with their
    /// dates, its stations with their names and their entrance and fixed flags. A <c>.3d</c>
    /// holds whole centimetres; the wall distances are left out, so every tube has the default
    /// radius.
    /// </summary>
    public static byte[] As3d(CaveModel model)
    {
        var file = Survex3dFixtureBuilder.V8("Invented cave").V8Style(0x00);
        DateOnly? current = null;
        bool labelled = false;
        foreach (var shot in model.Shots)
        {
            if (shot.DateFrom != current)
            {
                current = shot.DateFrom;
                if (current is { } day) file.V8SingleDate(day.DayNumber - Day1900.DayNumber);
                else file.Op(0x10);
            }
            int flags = ((shot.Flags & CaveShotFlags.Surface) != 0 ? 0x01 : 0) | ((shot.Flags & CaveShotFlags.Duplicate) != 0 ? 0x02 : 0)
                        | ((shot.Flags & CaveShotFlags.Splay) != 0 ? 0x04 : 0);
            file.V8Move(shot.FromPosition.X, shot.FromPosition.Y, shot.FromPosition.Z);
            file.V8Line(flags, labelled ? null : (0, "cave"), shot.ToPosition.X, shot.ToPosition.Y, shot.ToPosition.Z);
            labelled = true;
        }
        string label = labelled ? "cave" : "";
        foreach (var station in model.Stations)
        {
            int flags = 0x02 | (station.IsEntrance ? 0x04 : 0) | (station.IsFixed ? 0x10 : 0);
            file.V8Label(flags, System.Text.Encoding.UTF8.GetByteCount(label), station.Name, station.Position.X, station.Position.Y, station.Position.Z);
            label = station.Name;
        }
        return file.V8Stop().Build();
    }

    // ---- caves small enough to work out by hand ----

    private static CaveStation Station(uint id, string name, CaveVector3 at, CaveStationFlags flags = CaveStationFlags.None)
        => new() { Id = id, SurveyId = 0, Name = name, Position = at, Flags = flags };

    private static CaveShot Leg(IReadOnlyList<CaveStation> stations, int from, int to, CaveShotFlags flags = CaveShotFlags.None, DateOnly? day = null) => new()
    {
        FromStationId = stations[from].Id, ToStationId = stations[to].Id,
        FromPosition = stations[from].Position, ToPosition = stations[to].Position,
        SurveyId = 0, Flags = flags, DateFrom = day, DateTo = day,
    };

    /// <summary>A cave made of the stations at <paramref name="places"/> (named s0, s1, …; the
    /// ones in <paramref name="entrances"/> are entrances) and the legs between the pairs of
    /// <paramref name="legs"/>, each with its flags and its day.</summary>
    public static CaveModel Cave(
        IReadOnlyList<CaveVector3> places, IReadOnlyList<(int From, int To, CaveShotFlags Flags, DateOnly? Day)> legs, params int[] entrances)
    {
        var stations = places
            .Select((p, i) => Station((uint)i, "s" + i, p, entrances.Contains(i) ? CaveStationFlags.Entrance : CaveStationFlags.None))
            .ToList();
        return new CaveModel
        {
            SourceFormat = CaveSourceFormat.Survex3d,
            Surveys = [new CaveSurvey(0, 0, "", "Invented")],
            Stations = stations,
            Shots = legs.Select(l => Leg(stations, l.From, l.To, l.Flags, l.Day)).ToList(),
        };
    }

    /// <summary>A straight passage along X of legs ten metres long: s0 at the origin, sN at 10 N.</summary>
    public static CaveModel Straight(int legs, params int[] entrances)
        => Cave(
            Enumerable.Range(0, legs + 1).Select(i => new CaveVector3(10.0 * i, 0, 0)).ToList(),
            Enumerable.Range(0, legs).Select(i => (i, i + 1, CaveShotFlags.None, (DateOnly?)null)).ToList(),
            entrances);
}
