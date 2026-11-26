// An invented cave for tours, made by hand so that every length is a round number: a straight
// main passage with six side systems, one of them a loop, and a second part joined to nothing.
// Nothing here is surveyed data.
//
//   main   t0 (entrance) … t20, east, 10 m a leg: 200 m, level
//   north  n1 … n6 off t3, 10 m a leg: 60 m, a lead at its end
//   short  h1 off t6: 8 m
//   south  d1 … d5 off t9, 13 m a leg, 5 m down each: 65 m, 25 m down, narrow
//   loop   t9 – p1 – p2 – p3 – t13, 20 m a leg, with q1 10 m off p2: 90 m; a bypass
//   lead   x1 … x3 off t16, 10 m a leg: 30 m, a lead at its end
//   exit   y1, y2 off t18, 9 m a leg: 18 m, a second entrance at its end
//   far    f0 – f1: 15 m, a kilometre away

using Therion.Cave3D;

namespace Therion.Blender.Tests.SceneProbe;

internal static class TourCave
{
    private static readonly CaveVector3 Origin = new(512_000.0, 5_120_000.0, 1_200.0);

    public static CaveModel Build()
    {
        var surveys = new List<CaveSurvey> { new(0, 0, "", "Invented Tour Cave") };
        var stations = new List<CaveStation>();
        var shots = new List<CaveShot>();
        var byName = new Dictionary<string, int>();

        uint Survey(string name)
        {
            surveys.Add(new CaveSurvey((uint)surveys.Count, 0, name, name));
            return (uint)surveys.Count - 1;
        }
        void Station(uint survey, string name, double x, double y, double z, CaveStationFlags flags = CaveStationFlags.None, string? comment = null)
        {
            uint raw = 0;
            if ((flags & CaveStationFlags.Entrance) != 0) raw |= 2;
            if ((flags & CaveStationFlags.Fixed) != 0) raw |= 4;
            if ((flags & CaveStationFlags.Continuation) != 0) raw |= 8;
            byName[name] = stations.Count;
            stations.Add(new CaveStation
            {
                Id = (uint)stations.Count, SurveyId = survey, Name = name, Position = Origin + new CaveVector3(x, y, z),
                Flags = flags, RawFlags = raw, Comment = comment,
            });
        }
        void Leg(uint survey, string from, string to, CaveLrud lrud)
        {
            var (a, b) = (stations[byName[from]], stations[byName[to]]);
            shots.Add(new CaveShot
            {
                FromStationId = a.Id, ToStationId = b.Id, FromPosition = a.Position, ToPosition = b.Position, SurveyId = survey,
                SectionType = CaveShotSection.Oval, FromLrud = lrud, ToLrud = lrud, Threshold = 60.0,
            });
        }

        var wide = new CaveLrud(2, 2, 2, 1);        // 4 m wide
        var medium = new CaveLrud(1, 1.5, 1, 1);    // 2.5 m wide
        var narrow = new CaveLrud(0.5, 0.5, 0.5, 0.5);
        var even = new CaveLrud(1, 1, 1, 1);

        uint main = Survey("main");
        Station(main, "t0", 0, 0, 0, CaveStationFlags.Entrance | CaveStationFlags.Fixed);
        for (int i = 1; i <= 20; i++)
        {
            Station(main, $"t{i}", 10 * i, 0, 0);
            Leg(main, $"t{i - 1}", $"t{i}", wide);
        }

        uint north = Survey("north");
        for (int i = 1; i <= 6; i++)
        {
            Station(north, $"n{i}", 30, 10 * i, 0, i == 6 ? CaveStationFlags.Continuation : CaveStationFlags.None, i == 6 ? "Draughting crawl" : null);
            Leg(north, i == 1 ? "t3" : $"n{i - 1}", $"n{i}", medium);
        }

        uint brief = Survey("short");
        Station(brief, "h1", 60, -8, 0);
        Leg(brief, "t6", "h1", even);

        uint south = Survey("south");
        for (int i = 1; i <= 5; i++)
        {
            Station(south, $"d{i}", 90, -12 * i, -5 * i);
            Leg(south, i == 1 ? "t9" : $"d{i - 1}", $"d{i}", narrow);
        }

        uint loop = Survey("loop");
        Station(loop, "p1", 90, 20, 0);
        Station(loop, "p2", 110, 20, 0);
        Station(loop, "p3", 130, 20, 0);
        Station(loop, "q1", 110, 30, 0);
        Leg(loop, "t9", "p1", wide);
        Leg(loop, "p1", "p2", wide);
        Leg(loop, "p2", "p3", wide);
        Leg(loop, "p3", "t13", wide);
        Leg(loop, "p2", "q1", wide);

        uint lead = Survey("lead");
        for (int i = 1; i <= 3; i++)
        {
            Station(lead, $"x{i}", 160, 10 * i, 0, i == 3 ? CaveStationFlags.Continuation : CaveStationFlags.None);
            Leg(lead, i == 1 ? "t16" : $"x{i - 1}", $"x{i}", even);
        }

        uint exit = Survey("exit");
        Station(exit, "y1", 180, -9, 0);
        Station(exit, "y2", 180, -18, 0, CaveStationFlags.Entrance);
        Leg(exit, "t18", "y1", even);
        Leg(exit, "y1", "y2", even);

        uint far = Survey("far");
        Station(far, "f0", 1000, 1000, 0);
        Station(far, "f1", 1015, 1000, 0);
        Leg(far, "f0", "f1", even);

        return new CaveModel { SourceFormat = CaveSourceFormat.Lox, Surveys = surveys, Stations = stations, Shots = shots };
    }
}
