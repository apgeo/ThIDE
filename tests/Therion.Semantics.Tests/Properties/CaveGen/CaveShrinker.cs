// Narrows a failing case down before it is reported: switch off printing features one at a time,
// then drop child surveys, equates, fixes, notes, splays and titles, keeping each step only while
// the property still fails. Generated caves are large; the reduced one is what a person reads.

using System.Collections.Immutable;

namespace Therion.Semantics.Tests.Properties.CaveGen;

internal static class CaveShrinker
{
    public sealed record Reduced(GCave Cave, PrintFeatures Features, List<PrintedFile> Files, List<string> Problems);

    public static Reduced Shrink(GCave cave, PrintFeatures features, int printSeed,
        Func<GCave, List<PrintedFile>, List<string>> check, int budget = 300)
    {
        List<string> Run(GCave c, PrintFeatures f, out List<PrintedFile> files)
        {
            files = CavePrinter.Print(Clone(c), f, printSeed);
            return check(c, files);
        }

        var problems = Run(cave, features, out var printed);
        foreach (var name in PrintFeatures.Names)
        {
            if (!features.Get(name) || budget-- <= 0) continue;
            var fewer = features.With(name, false);
            var p = Run(cave, fewer, out var files);
            if (p.Count > 0) (features, problems, printed) = (fewer, p, files);
        }

        bool progress = true;
        while (progress && budget > 0)
        {
            progress = false;
            foreach (var step in Steps(cave))
            {
                if (budget-- <= 0) break;
                var smaller = step(Clone(cave));
                var p = Run(smaller, features, out var files);
                if (p.Count == 0) continue;
                (cave, problems, printed) = (smaller, p, files);
                progress = true;
                break;
            }
        }
        return new Reduced(cave, features, printed, problems);
    }

    private static IEnumerable<Func<GCave, GCave>> Steps(GCave cave)
    {
        foreach (var s in cave.Surveys.Skip(1).Reverse().ToList())
        {
            var path = s.Path;
            yield return c => DropSurvey(c, path);
        }
        foreach (var s in cave.Surveys.ToList())
        {
            var path = s.Path;
            if (s.Equates.Count > 0) yield return c => Edit(c, path, x => x.Equates.Clear());
            if (s.Fixes.Count > 0) yield return c => Edit(c, path, x => x.Fixes.Clear());
            if (s.Notes.Count > 0) yield return c => Edit(c, path, x => x.Notes.Clear());
            if (s.Shots.Any(sh => sh.To is null)) yield return c => Edit(c, path, x => x.Shots.RemoveAll(sh => sh.To is null));
            if (s.Shots.Any(sh => sh.To is not null && !sh.To.Survey.SequenceEqual(path)))
                yield return c => Edit(c, path, x => x.Shots.RemoveAll(sh => sh.To is not null && !sh.To.Survey.SequenceEqual(path)));
            if (s.Shots.Any(sh => sh.Flags != ShotFlags.None))
                yield return c => Edit(c, path, x =>
                {
                    for (int i = 0; i < x.Shots.Count; i++) x.Shots[i] = x.Shots[i] with { Flags = ShotFlags.None };
                });
            if (s.Title is not null) yield return c => Edit(c, path, x => x.Title = null);
        }
    }

    private static GCave Edit(GCave cave, ImmutableArray<string> path, Action<GSurvey> edit)
    {
        edit(cave.Surveys.First(s => s.Path.SequenceEqual(path)));
        return cave;
    }

    private static GCave DropSurvey(GCave cave, ImmutableArray<string> path)
    {
        bool Inside(GRef r) => r.Survey.Length >= path.Length && r.Survey.Take(path.Length).SequenceEqual(path);
        foreach (var s in cave.Surveys.ToList())
        {
            s.Children.RemoveAll(c => c.Path.SequenceEqual(path));
            s.Shots.RemoveAll(sh => Inside(sh.From) || sh.To is not null && Inside(sh.To));
            s.Fixes.RemoveAll(f => Inside(f.Station));
            s.Notes.RemoveAll(n => Inside(n.Station));
            for (int i = s.Equates.Count - 1; i >= 0; i--)
            {
                var kept = s.Equates[i].Members.Where(m => !Inside(m)).ToImmutableArray();
                if (kept.Length < 2) s.Equates.RemoveAt(i);
                else s.Equates[i] = new GEquate(kept);
            }
        }
        return cave;
    }

    public static GCave Clone(GCave cave) => new(cave.Seed, Clone(cave.Root));

    private static GSurvey Clone(GSurvey s)
    {
        var c = new GSurvey(s.Path) { Title = s.Title };
        c.Stations.AddRange(s.Stations);
        c.Shots.AddRange(s.Shots);
        c.Equates.AddRange(s.Equates);
        c.Fixes.AddRange(s.Fixes);
        c.Notes.AddRange(s.Notes);
        foreach (var child in s.Children) c.Children.Add(Clone(child));
        return c;
    }
}
