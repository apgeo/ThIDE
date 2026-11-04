// Crash recovery as a state machine. Seeded random runs of up to three app instances share one
// recovery folder (a temp folder, never the user profile). Each instance edits invented files through
// a real DocumentService, autosaves its dirty buffers, saves, crashes, exits (discarding, or keeping
// the copies because it could not ask), and restores or dismisses what it was offered at start the way
// the main window does. A model tracks which copies must exist; after every step each live instance's
// GetRecoverable must be exactly its own last autosave plus every unhandled copy of an ended run.
//
// Guarantees checked:
//   1. a copy left by a crashed run (or an exit that kept them) is offered by every later start until
//      it is restored or dismissed;
//   2. a live instance's copies are never offered by, or deleted by, another instance;
//   3. PreviousRunCrashed is true exactly for the first start after a run died;
//   4. Restore never replaces a newer dirty buffer: it opens the recovered text beside it;
//   5. a clean exit leaves no copy of its own behind;
//   6. restoring or dismissing settles only the runs the offer was made from, and until the next
//      autosave a crash still recovers the settled copies and the run's own.
// The instance death is simulated by closing the run lock the OS would release when the process ends.

using System.Collections.Immutable;
using System.Reflection;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class CrashRecoveryLifecycleProperties
{
    private const int Runs = 200;
    private const int Steps = 20;
    private const int PerBatch = 25;
    private const int BaseSeed = 0x0C4A_5E07;

    public static IEnumerable<object[]> Batches() => Enumerable.Range(0, Runs / PerBatch).Select(b => new object[] { b });

    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    /// <summary>A copy that exists on disk for an ended run and has not been restored or dismissed.</summary>
    private sealed record Unhandled(int Run, string Path, string Text);

    private sealed class Instance
    {
        public required int Id { get; init; }
        public required CrashRecoveryService Recovery { get; init; }
        public required DocumentService Documents { get; init; }
        /// <summary>What this run's last autosave wrote (path → text).</summary>
        public Dictionary<string, string> OwnCopies { get; } = new(StringComparer.Ordinal);
        /// <summary>Copies of the offer this run restored or dismissed, kept with it until its next autosave.</summary>
        public List<(string Path, string Text)> Settled { get; } = new();
        /// <summary>The buffers offered at start, while the user has not acted on them.</summary>
        public IReadOnlyList<RecoveredBuffer>? Offer { get; set; }
        public List<Unhandled> OfferedCopies { get; set; } = new();
    }

    private sealed class World : IDisposable
    {
        public World(int seed)
        {
            Seed = seed;
            Root = Directory.CreateTempSubdirectory("thide-recovery-lifecycle-").FullName;
            Profile = Path.Combine(Root, "profile");
            Project = Directory.CreateDirectory(Path.Combine(Root, "project")).FullName;
            for (int i = 0; i < 4; i++)
            {
                var path = Path.Combine(Project, $"f{i}.th");
                File.WriteAllText(path, $"survey f{i}\n  # invented\nendsurvey\n");
                Files.Add(path);
            }
        }

        public int Seed { get; }
        public string Root { get; }
        public string Profile { get; }
        public string Project { get; }
        public List<string> Files { get; } = new();
        public List<Instance> Live { get; } = new();
        public List<Unhandled> Pool { get; } = new();
        /// <summary>Lock files of runs that died and that no start has seen yet.</summary>
        public int UnseenCrashes { get; set; }
        public int NextId { get; set; }
        public List<string> Trace { get; } = new();
        public Dictionary<string, int> Reached { get; init; } = new(StringComparer.Ordinal);

        public void Count(string shape) => Reached[shape] = Reached.GetValueOrDefault(shape) + 1;

        public string Context => $"seed {Seed}, steps: {string.Join(" → ", Trace)}";

        /// <summary>
        /// Every instance's workspace session, crashed ones included. A session that opened a file watches
        /// the project folder, and on Linux each watcher holds an inotify instance until it is disposed;
        /// hundreds of leaked ones exhaust the per-user limit and break watchers in other tests and processes.
        /// </summary>
        public List<WorkspaceSessionService> Sessions { get; } = new();

        public void Dispose()
        {
            foreach (var session in Sessions) session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private enum Op { Start, Edit, Autosave, SaveDocument, Crash, ExitDiscarding, ExitKeepingCopies, Restore, Dismiss }

    [Theory]
    [MemberData(nameof(Batches))]
    public async Task Recovery_copies_follow_the_lifecycle_model(int batch)
    {
        var reached = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var seed in Enumerable.Range(BaseSeed + batch * PerBatch, PerBatch))
        {
            using var world = new World(seed) { Reached = reached };
            var rnd = new Random(seed);
            for (int step = 0; step < Steps; step++)
            {
                var op = Choose(world, rnd);
                world.Trace.Add(op.ToString());
                await ApplyAsync(world, op, rnd, step);
                CheckInvariants(world);
            }

            // Everything ends cleanly; a later start still offers exactly the unhandled copies.
            foreach (var live in world.Live.ToList()) await ApplyExitAsync(world, live, keepCopies: false);
            var probe = new CrashRecoveryService(world.Profile);
            probe.MarkRunning();
            Assert.True(SameCopies(world.Pool.Select(u => (u.Path, u.Text)), probe.GetRecoverable()),
                $"after every instance exited, the next start does not offer exactly the unhandled copies. {world.Context}");
            probe.MarkCleanShutdown();
        }

        // The batch reached the transitions the guarantees are about, not only the easy ones.
        foreach (var shape in new[] { "offer", "restore", "dismiss", "restore beside newer edits", "concurrent instances", "crash with copies", "exit keeping copies" })
            Assert.True(reached.GetValueOrDefault(shape) > 0,
                $"no run of the batch reached '{shape}': {string.Join(", ", reached.Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    private static Op Choose(World world, Random rnd)
    {
        var weighted = new List<(Op Op, int Weight)>();
        if (world.Live.Count < 3) weighted.Add((Op.Start, world.Live.Count == 0 ? 6 : 1));
        if (world.Live.Count > 0)
        {
            weighted.Add((Op.Edit, 5));
            weighted.Add((Op.Autosave, 4));
            weighted.Add((Op.Crash, 2));
            weighted.Add((Op.ExitDiscarding, 1));
            weighted.Add((Op.ExitKeepingCopies, 1));
            if (world.Live.Any(i => i.Documents.Documents.Any(d => d.IsDirty))) weighted.Add((Op.SaveDocument, 1));
            if (world.Live.Any(i => CanHandleOffer(i)))
            {
                weighted.Add((Op.Restore, 3));
                weighted.Add((Op.Dismiss, 1));
            }
        }
        int pick = rnd.Next(weighted.Sum(w => w.Weight));
        foreach (var (op, weight) in weighted)
        {
            if (pick < weight) return op;
            pick -= weight;
        }
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// An offer can be acted on at any time, also after other runs have ended with copies since it was
    /// made: those must stay for the next start (see the facts below).
    /// </summary>
    private static bool CanHandleOffer(Instance i) => i.Offer is { Count: > 0 };

    private static async Task ApplyAsync(World world, Op op, Random rnd, int step)
    {
        switch (op)
        {
            case Op.Start:
                Start(world);
                break;

            case Op.Edit:
            {
                var inst = Pick(world.Live, rnd);
                var candidates = world.Files.Concat(inst.Documents.Documents.Select(d => d.FilePath)).Distinct().ToList();
                // Often edit a file the pending offer has a copy of: the restore then meets newer edits.
                var offered = inst.Offer?.Select(o => o.OriginalPath).Where(candidates.Contains).ToList() ?? new();
                var path = offered.Count > 0 && rnd.NextDouble() < 0.5 ? Pick(offered, rnd) : Pick(candidates, rnd);
                var doc = Find(inst, path);
                if (doc is null)
                {
                    await inst.Documents.OpenFileAsync(path);
                    doc = Find(inst, path) ?? throw new InvalidOperationException($"{path} did not open. {world.Context}");
                }
                doc.DocumentText = $"survey edited\n  # run {inst.Id}, step {step}, seed {world.Seed}\nendsurvey\n";
                break;
            }

            case Op.Autosave:
                Autosave(Pick(world.Live, rnd));
                break;

            case Op.SaveDocument:
            {
                var inst = Pick(world.Live.Where(i => i.Documents.Documents.Any(d => d.IsDirty)).ToList(), rnd);
                var doc = Pick(inst.Documents.Documents.Where(d => d.IsDirty).ToList(), rnd);
                Assert.True(await inst.Documents.SaveDocumentAsync(doc), $"save failed. {world.Context}");
                break;
            }

            case Op.Crash:
            {
                var inst = Pick(world.Live, rnd);
                if (inst.OwnCopies.Count > 0) world.Count("crash with copies");
                SimulateProcessDeath(inst.Recovery);
                world.Live.Remove(inst);
                AddToPool(world, inst);
                world.UnseenCrashes++;
                await inst.Documents.DisposeAsync();
                break;
            }

            case Op.ExitDiscarding:
                await ApplyExitAsync(world, Pick(world.Live, rnd), keepCopies: false);
                break;

            case Op.ExitKeepingCopies:
                await ApplyExitAsync(world, Pick(world.Live, rnd), keepCopies: true);
                break;

            case Op.Restore:
                await RestoreAsync(world, Pick(world.Live.Where(i => CanHandleOffer(i)).ToList(), rnd));
                break;

            case Op.Dismiss:
            {
                var inst = Pick(world.Live.Where(i => CanHandleOffer(i)).ToList(), rnd);
                inst.Recovery.ClearRecovery();
                Handled(world, inst);
                world.Count("dismiss");
                break;
            }
        }
    }

    private static Instance Start(World world)
    {
        var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        world.Sessions.Add(session);
        var inst = new Instance
        {
            Id = world.NextId++,
            Recovery = new CrashRecoveryService(world.Profile),
            Documents = new DocumentService(new StubResolver(), session),
        };
        inst.Recovery.MarkRunning();
        Assert.True((world.UnseenCrashes > 0) == inst.Recovery.PreviousRunCrashed,
            $"PreviousRunCrashed is {inst.Recovery.PreviousRunCrashed} with {world.UnseenCrashes} unseen crashed run(s). {world.Context}");
        world.UnseenCrashes = 0;

        var offered = inst.Recovery.GetRecoverable();
        Assert.True(SameCopies(world.Pool.Select(u => (u.Path, u.Text)), offered),
            $"a start offers {Describe(offered.Select(b => (b.OriginalPath, b.Text)))} but the unhandled copies are " +
            $"{Describe(world.Pool.Select(u => (u.Path, u.Text)))}. {world.Context}");
        if (world.Live.Count > 0) world.Count("concurrent instances");
        if (offered.Count > 0)
        {
            world.Count("offer");
            inst.Offer = offered;
            inst.OfferedCopies = world.Pool.ToList();
        }
        world.Live.Add(inst);
        return inst;
    }

    private static void Autosave(Instance inst)
    {
        var dirty = inst.Documents.Documents
            .Where(d => d.IsDirty && !string.IsNullOrEmpty(d.FilePath))
            .Select(d => (d.FilePath, d.DocumentText))
            .ToList();
        inst.Recovery.SaveBuffers(dirty);
        inst.OwnCopies.Clear();
        foreach (var (path, text) in dirty) inst.OwnCopies[path] = text;
        inst.Settled.Clear();
    }

    private static async Task ApplyExitAsync(World world, Instance inst, bool keepCopies)
    {
        if (keepCopies)
        {
            // An exit that could not ask about unsaved buffers: one last autosave, and the copies stay.
            Autosave(inst);
            if (inst.OwnCopies.Count > 0) world.Count("exit keeping copies");
            inst.Recovery.MarkCleanShutdown(keepRecovery: true);
            AddToPool(world, inst);
        }
        else
        {
            // The user saved or discarded everything: nothing of this run may stay behind.
            inst.Recovery.MarkCleanShutdown();
        }
        world.Live.Remove(inst);
        await inst.Documents.DisposeAsync();
    }

    private static void AddToPool(World world, Instance inst)
    {
        var copies = inst.OwnCopies.Select(kv => (kv.Key, kv.Value)).Concat(inst.Settled).ToList();
        if (copies.Count == 0) return;
        foreach (var (path, text) in copies) world.Pool.Add(new Unhandled(inst.Id, path, text));
    }

    /// <summary>What the main window's Restore does: reopen every offered buffer, then clear the copies when all reopened.</summary>
    private static async Task RestoreAsync(World world, Instance inst)
    {
        var offer = inst.Offer!;
        var dirtyBefore = inst.Documents.Documents.Where(d => d.IsDirty).Select(d => (Doc: d, Text: d.DocumentText)).ToList();
        var restored = new List<(FileDocumentViewModel Doc, string Text)>();
        int recovered = 0;
        foreach (var b in offer)
        {
            if (Find(inst, b.OriginalPath) is { IsDirty: true } open && open.DocumentText != b.Text)
                world.Count("restore beside newer edits");
            var doc = await inst.Documents.OpenRecoveredAsync(b.OriginalPath, b.Text);
            Assert.True(b.Text == doc.DocumentText, $"restore of {b.OriginalPath} did not open its text. {world.Context}");
            restored.Add((doc, b.Text));
            recovered++;
        }

        foreach (var (doc, text) in dirtyBefore)
        {
            Assert.True(inst.Documents.Documents.Contains(doc) && doc.DocumentText == text,
                $"restore replaced or closed newer unsaved edits in {doc.FilePath}. {world.Context}");
        }
        foreach (var (doc, text) in restored)
        {
            Assert.True(inst.Documents.Documents.Contains(doc) && doc.DocumentText == text,
                $"a restored buffer for {doc.FilePath} was replaced by a later one of the same restore. {world.Context}");
        }

        if (recovered == offer.Count) inst.Recovery.ClearRecovery();
        Handled(world, inst);
        world.Count("restore");
    }

    /// <summary>
    /// The user restored or dismissed the offer: its copies are no longer offered, but stay with this run
    /// until its next autosave; this run's own copies are untouched.
    /// </summary>
    private static void Handled(World world, Instance inst)
    {
        inst.Settled.AddRange(inst.OfferedCopies.Select(c => (c.Path, c.Text)));
        world.Pool.RemoveAll(inst.OfferedCopies.Contains);
        inst.Offer = null;
        inst.OfferedCopies = new();
        foreach (var other in world.Live.Where(o => o != inst && o.Offer is not null))
            other.OfferedCopies.RemoveAll(c => !world.Pool.Contains(c));
    }

    private static void CheckInvariants(World world)
    {
        foreach (var inst in world.Live)
        {
            var expected = inst.OwnCopies.Select(kv => (kv.Key, kv.Value)).Concat(world.Pool.Select(u => (u.Path, u.Text)));
            var actual = inst.Recovery.GetRecoverable();
            Assert.True(SameCopies(expected, actual),
                $"instance {inst.Id} sees {Describe(actual.Select(b => (b.OriginalPath, b.Text)))} but expected its own " +
                $"{Describe(inst.OwnCopies.Select(kv => (kv.Key, kv.Value)))} plus the unhandled " +
                $"{Describe(world.Pool.Select(u => (u.Path, u.Text)))}. {world.Context}");
        }

        int locks = Directory.Exists(world.Profile) ? Directory.GetFiles(world.Profile, "running.*.lock").Length : 0;
        Assert.True(locks == world.Live.Count + world.UnseenCrashes,
            $"{locks} run lock(s) for {world.Live.Count} live instance(s) and {world.UnseenCrashes} unseen crash(es). {world.Context}");
    }

    private static bool SameCopies(IEnumerable<(string Path, string Text)> expected, IReadOnlyList<RecoveredBuffer> actual)
    {
        static string Key((string Path, string Text) c) => c.Path + "\0" + c.Text;
        var e = expected.Select(Key).Order(StringComparer.Ordinal);
        var a = actual.Select(b => Key((b.OriginalPath, b.Text))).Order(StringComparer.Ordinal);
        return e.SequenceEqual(a);
    }

    private static string Describe(IEnumerable<(string Path, string Text)> copies) =>
        "[" + string.Join(", ", copies.Select(c => $"{Path.GetFileName(c.Path)} \"{c.Text.Replace("\n", "\\n")}\"")) + "]";

    private static FileDocumentViewModel? Find(Instance inst, string path) =>
        inst.Documents.Documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.Ordinal));

    private static T Pick<T>(IReadOnlyList<T> items, Random rnd) => items[rnd.Next(items.Count)];

    /// <summary>
    /// What the OS does when the process dies: the run lock it held open is released, and the lock
    /// file and the recovery copies stay on disk. Nothing else of the instance runs again.
    /// </summary>
    private static void SimulateProcessDeath(CrashRecoveryService recovery)
    {
        var field = typeof(CrashRecoveryService).GetField("_lock", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CrashRecoveryService no longer holds its run lock in '_lock'; update the crash simulation.");
        (field.GetValue(recovery) as IDisposable)?.Dispose();
        field.SetValue(recovery, null);
    }

    // ---- Lifecycle gaps found by the model, kept as facts --------------------------------------

    private static (World World, Instance Survivor) WorldWithAnOffer(int seed, string crashedText)
    {
        var world = new World(seed);
        var crashed = Start(world);
        crashed.OwnCopies[world.Files[0]] = crashedText;
        crashed.Recovery.SaveBuffers([(world.Files[0], crashedText)]);
        SimulateProcessDeath(crashed.Recovery);
        world.Live.Remove(crashed);
        AddToPool(world, crashed);
        world.UnseenCrashes++;
        var survivor = Start(world);
        Assert.NotNull(survivor.Offer);
        return (world, survivor);
    }

    [Fact]
    public void Acting_on_an_offer_keeps_the_copies_of_a_run_that_crashed_after_it_was_shown()
    {
        var (world, first) = WorldWithAnOffer(1, "survey f0\n  # crashed before the first start\nendsurvey\n");
        using var _ = world;

        // A second instance runs alongside, autosaves an edit and dies, while the first still shows its offer.
        var second = Start(world);
        const string laterText = "survey f1\n  # crashed while the first instance was running\nendsurvey\n";
        second.Recovery.SaveBuffers([(world.Files[1], laterText)]);
        SimulateProcessDeath(second.Recovery);

        first.Recovery.ClearRecovery();   // the user dismisses the offer they saw at start
        first.Recovery.MarkCleanShutdown();

        var next = new CrashRecoveryService(world.Profile);
        next.MarkRunning();
        Assert.Contains(next.GetRecoverable(), b => b.Text == laterText);
        next.MarkCleanShutdown();
    }

    [Fact]
    public async Task Buffers_restored_or_still_unsaved_stay_recoverable_right_after_an_offer_is_handled()
    {
        const string crashedText = "survey f0\n  # crashed before this start\nendsurvey\n";
        var (world, inst) = WorldWithAnOffer(2, crashedText);
        using var _ = world;

        // An unsaved edit of this run, already autosaved.
        await inst.Documents.OpenFileAsync(world.Files[2]);
        const string ownText = "survey f2\n  # unsaved edit of this run\nendsurvey\n";
        Find(inst, world.Files[2])!.DocumentText = ownText;
        Autosave(inst);

        await RestoreAsync(world, inst);   // reopens the crashed text and clears the copies
        SimulateProcessDeath(inst.Recovery);   // dies before the next 5-second autosave

        var next = new CrashRecoveryService(world.Profile);
        next.MarkRunning();
        var offered = next.GetRecoverable();
        next.MarkCleanShutdown();
        Assert.Contains(offered, b => b.Text == crashedText);
        Assert.Contains(offered, b => b.Text == ownText);
    }
}
