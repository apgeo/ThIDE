// The workspace session under generated schedules of events.
//
// Activations: thconfig activations are requested one after another (now and then a missing file, or
// a thconfig in another folder) while earlier ones are still loading, and the loads are let finish in
// a random order. After every step: the active thconfig and the published model are those of the
// latest request that has finished, an older activation that finishes later never replaces it,
// Changed is raised once per published activation and after its model is in place, and IsIndexing is
// on exactly while an activation is still loading. At the end the active thconfig is the one asked for
// last and IsIndexing is off.
//
// External changes: bursts of rewrites over random subsets of the tracked files, one burst with a
// Changed subscriber that throws, then the IDE's own save followed half a second later by somebody
// else's write. Each burst is published once or twice (not once per file), the model published last
// holds every rewrite, the rebuild after the throwing subscriber still happens, nothing is still
// pending or indexing at rest, and the write after the own save is reported.
//
// Seeds are fixed (THIDE_PROPERTY_SEED overrides the base); a failure names the seed and the steps.

using System.Collections.Concurrent;
using ThIDE.Services;

namespace ThIDE.Tests;

public class WorkspaceSessionScheduleProperties
{
    private const int DefaultSeed = 0x5E55_1016;
    private const int ActivationCases = 25;
    private const int BurstCases = 2;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static int BaseSeed =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : DefaultSeed;

    private static string Survey(string name) =>
        $"survey {name}\n  centreline\n    1 2 5 0 0\n  endcentreline\nendsurvey\n";

    private static string Full(string p) => Path.GetFullPath(p);

    // ---- overlapping activations ----------------------------------------------------------------

    private sealed record Pending(int Id, string Path, TaskCompletionSource Release, Task<bool> Activation);

    [Fact]
    public async Task The_latest_finished_request_is_active_and_the_last_request_wins()
    {
        var failures = new List<string>();
        for (int i = 0; i < ActivationCases; i++)
        {
            int seed = BaseSeed + i;
            var trace = new List<string>();
            try
            {
                var message = await RunActivationCase(seed, trace);
                if (message is not null) failures.Add($"seed {seed} (base {BaseSeed}): {message}\n  steps: {string.Join(" → ", trace)}");
            }
            catch (Exception ex) { failures.Add($"seed {seed} (base {BaseSeed}): threw {ex}\n  steps: {string.Join(" → ", trace)}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(6)));
    }

    private static async Task<string?> RunActivationCase(int seed, List<string> trace)
    {
        var rng = new Random(seed);
        using var dir = new TempDir();
        using var elsewhere = new TempDir();
        var configs = new List<string>();
        for (int k = 0; k < 4; k++)
        {
            var rel = k == 3 ? $"sub/c{k}.thconfig" : $"c{k}.thconfig";
            dir.Write(Path.Combine(Path.GetDirectoryName(rel)!, $"s{k}.th"), Survey($"s{k}"));
            configs.Add(dir.Write(rel, $"encoding utf-8\nsource s{k}.th\n"));
        }
        elsewhere.Write("s9.th", Survey("s9"));
        var outside = elsewhere.Write("c9.thconfig", "encoding utf-8\nsource s9.th\n");
        string SurveyOf(string cfg) => "s" + Path.GetFileNameWithoutExtension(cfg)[1..];

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(dir.Path);
        string? active = session.ActiveThconfig?.FullPath;
        if (active is null) return "the root picked no thconfig";

        var entered = new BlockingCollection<(string Path, TaskCompletionSource Release)>();
        session.ActivationLoadedForTests = path =>
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            entered.Add((path, release));
            return release.Task;
        };
        var published = new ConcurrentQueue<(string? Active, bool HasItsSurvey)>();
        session.Changed += (_, _) =>
        {
            var a = session.ActiveThconfig?.FullPath;
            published.Enqueue((a, a is not null && session.Model?.SurveysByFullName.ContainsKey(SurveyOf(a)) == true));
        };

        var pending = new List<Pending>();
        int latest = 0, nextId = 0, expectedPublishes = 0;
        string? lastRequested = null;

        async Task<string?> Release(Pending p)
        {
            pending.Remove(p);
            trace.Add($"finish #{p.Id}");
            p.Release.SetResult();
            var finished = await Task.WhenAny(p.Activation, Task.Delay(Patience));
            if (finished != p.Activation) return $"activation #{p.Id} did not finish after its load was released";
            if (!await p.Activation) return $"activation #{p.Id} of an existing thconfig reported a failure";
            if (p.Id == latest) { active = p.Path; expectedPublishes++; }
            return null;
        }

        string? Check(string when)
        {
            if (!string.Equals(session.ActiveThconfig?.FullPath is { } a ? Full(a) : null, Full(active!), StringComparison.Ordinal))
                return $"{when}: active is {session.ActiveThconfig?.FullPath ?? "none"}, expected {active}";
            if (session.Model?.SurveysByFullName.ContainsKey(SurveyOf(active!)) != true)
                return $"{when}: the published model is not the one of {Path.GetFileName(active)}";
            if (published.Count != expectedPublishes)
                return $"{when}: Changed raised {published.Count} time(s), {expectedPublishes} activation(s) published";
            if (published.TryPeek(out _) && published.Last() is var last && (last.Active is null || Full(last.Active) != Full(active!) || !last.HasItsSurvey))
                return $"{when}: the last Changed was raised before the model of {Path.GetFileName(active)} was in place";
            if (session.IsIndexing != pending.Count > 0)
                return $"{when}: IsIndexing is {session.IsIndexing} with {pending.Count} activation(s) still loading";
            return null;
        }

        for (int step = rng.Next(5, 11); step > 0; step--)
        {
            string? problem;
            if (pending.Count == 0 || rng.Next(5) < 3)
            {
                if (rng.Next(8) == 0)
                {
                    trace.Add("request missing");
                    if (await session.SetActiveThconfigAsync(Path.Combine(dir.Path, "gone.thconfig")))
                        return "activating a missing thconfig reported success";
                    problem = Check("after a missing thconfig was asked for");
                }
                else
                {
                    var path = rng.Next(6) == 0 ? outside : configs[rng.Next(configs.Count)];
                    int id = ++nextId;
                    latest = id;
                    lastRequested = path;
                    trace.Add($"request #{id} {Path.GetFileName(path)}");
                    var activation = Task.Run(() => session.SetActiveThconfigAsync(path));
                    if (!entered.TryTake(out var hold, Patience)) return $"activation #{id} never finished loading its thconfig";
                    if (Full(hold.Path) != Full(path)) return $"activation #{id} loaded {hold.Path}";
                    pending.Add(new Pending(id, path, hold.Release, activation));
                    problem = Check($"after request #{id}");
                }
            }
            else
            {
                var p = pending[rng.Next(pending.Count)];
                problem = await Release(p) ?? Check($"after #{p.Id} finished");
            }
            if (problem is not null) return problem;
        }
        while (pending.Count > 0)
        {
            var p = pending[rng.Next(pending.Count)];
            var problem = await Release(p) ?? Check($"after #{p.Id} finished");
            if (problem is not null) return problem;
        }

        if (lastRequested is not null && Full(session.ActiveThconfig!.FullPath) != Full(lastRequested))
            return $"at rest the active thconfig is {session.ActiveThconfig.FullPath}, the last one asked for was {lastRequested}";
        if (session.IsIndexing) return "IsIndexing is still on at rest";
        return null;
    }

    // ---- bursts of external changes -------------------------------------------------------------

    [Fact]
    public async Task Bursts_of_external_changes_are_published_once_each_and_none_is_lost()
    {
        var failures = new List<string>();
        for (int i = 0; i < BurstCases; i++)
        {
            int seed = BaseSeed + 500 + i;
            try
            {
                var message = await RunBurstCase(seed);
                if (message is not null) failures.Add($"seed {seed} (base {BaseSeed}): {message}");
            }
            catch (Exception ex) { failures.Add($"seed {seed} (base {BaseSeed}): threw {ex}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static async Task<string?> RunBurstCase(int seed)
    {
        var rng = new Random(seed);
        using var dir = new TempDir();
        const int Files = 6;
        var sources = new string[Files];
        var names = new string[Files];
        var config = "encoding utf-8\n";
        for (int k = 0; k < Files; k++)
        {
            names[k] = $"f{k}_0";
            sources[k] = dir.Write($"f{k}.th", Survey(names[k]));
            config += $"source f{k}.th\n";
        }
        var thconfig = dir.Write("cave.thconfig", config);

        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        // Wide enough that a burst written in a tight loop is one burst on a loaded machine too.
        session.RebuildQuietPeriod = TimeSpan.FromMilliseconds(600);
        await session.SetRootAsync(dir.Path);
        if (!await session.SetActiveThconfigAsync(thconfig)) return "the project did not load";

        int changed = 0, armed = 0;
        var lastPublished = new string[0];
        session.Changed += (_, _) =>
        {
            Interlocked.Increment(ref changed);
            lastPublished = session.Model?.SurveysByFullName.Keys.ToArray() ?? [];
        };
        session.Changed += (_, _) =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 1) throw new InvalidOperationException("subscriber fault");
        };
        var reported = new ConcurrentQueue<string>();
        session.ExternalFileChanged += (_, e) => reported.Enqueue(Full(e.Path));

        bool AllPublished() =>
            !session.IsRebuildPending && session.Model is { } m && names.All(n => m.SurveysByFullName.ContainsKey(n));

        int bursts = 3, throwingBurst = rng.Next(bursts);
        for (int b = 1; b <= bursts; b++)
        {
            var subset = Enumerable.Range(0, Files).Where(_ => rng.Next(2) == 0).ToList();
            if (subset.Count == 0) subset.Add(rng.Next(Files));
            if (b - 1 == throwingBurst) Volatile.Write(ref armed, 1);
            int before = Volatile.Read(ref changed);
            foreach (var k in subset)
            {
                names[k] = $"f{k}_{b}";
                File.WriteAllText(sources[k], Survey(names[k]));
            }
            var until = DateTime.UtcNow + Patience;
            while (!AllPublished() && DateTime.UtcNow < until) await Task.Delay(20);
            var desc = $"burst {b} over [{string.Join(", ", subset.Select(k => $"f{k}"))}]{(b - 1 == throwingBurst ? " (subscriber throws)" : "")}";
            if (!AllPublished())
                return $"{desc}: the rewrites never all reached the published model{(b - 1 > throwingBurst ? " after a subscriber threw" : "")}";
            int published = Volatile.Read(ref changed) - before;
            if (published is < 1 or > 2) return $"{desc}: published {published} times for one burst of {subset.Count} file(s)";
            if (!names.All(n => lastPublished.Contains(n))) return $"{desc}: the model passed to the last Changed misses a rewrite";
            if (session.IsIndexing) return $"{desc}: IsIndexing is still on at rest";
        }

        // The IDE's own save, then somebody else's write to the same file half a second later.
        var target = sources[rng.Next(Files)];
        while (reported.TryDequeue(out _)) { }
        session.SuppressSelfWrite(target);
        File.WriteAllText(target, Survey("own_save"));
        var seen = DateTime.UtcNow + Patience;
        while (session.IsSelfWriteUnseen(target) && DateTime.UtcNow < seen) await Task.Delay(20);
        if (session.IsSelfWriteUnseen(target)) return "the watcher never delivered the IDE's own save";
        await Task.Delay(500);
        if (reported.Contains(Full(target))) return $"the IDE's own save of {Path.GetFileName(target)} was reported as an external change";
        File.WriteAllText(target, Survey("their_write"));
        var reportedBy = DateTime.UtcNow + Patience;
        while (!reported.Contains(Full(target)) && DateTime.UtcNow < reportedBy) await Task.Delay(20);
        if (!reported.Contains(Full(target))) return $"a write 0.5 s after the IDE's own save of {Path.GetFileName(target)} was not reported";
        return null;
    }
}
