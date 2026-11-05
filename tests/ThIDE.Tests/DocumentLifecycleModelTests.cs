// The document lifecycle as a model: no silent loss.
//
// Seeded runs of one app at a time over four invented files in a temp project (UTF-8, a declared
// ISO-8859-2 file, a file with a byte UTF-8 cannot decode, and one in a folder that is made read-only
// for some saves) and a temp recovery profile — never the user's. Each run interleaves:
//   - open and type;
//   - save, which succeeds, is refused (a character the file's encoding cannot hold, or a U+FFFD left
//     in a file read lossily), fails (read-only folder) or writes again a file deleted on disk;
//   - external writes and deletes, the watcher's notice delivered at once, later or not at all, and the
//     changed-on-disk banner's reload over unsaved edits;
//   - closing a tab with Save / Don't save / Cancel (the dock's guard: SaveBeforeClose, then close);
//   - the 5-second recovery autosave;
//   - exit through the main window's ExitFlow: Save all / Don't save / Cancel, a second close request
//     while the question is up, an OS shutdown (also in the middle of the question);
//   - a crash (every service dropped, the recovery profile kept) and the next start restoring,
//     dismissing or ignoring what it offers (the main window's RecoverBuffersAsync replayed).
//
// Checked after every step:
//   1. no silent loss: the last text typed into a document is in its buffer or on disk, and a copy
//      kept from an ended run is open, on disk or still offered — unless the user chose Don't save,
//      a reload over it or Discard on the offer after typing it. A crash loses at most what was typed
//      since the last recovery autosave: the next start offers what that autosave held;
//   2. the disk holds exactly the last complete write (the original file, a save, an external write);
//      a refused or failed save leaves the bytes alone and no stray file behind;
//   3. a save succeeds exactly when it can, and a document is unsaved exactly when its buffer differs
//      from what it last read or saved.
// THIDE_PROPERTY_RUNS sets the number of seeds; THIDE_PROPERTY_SEED replays one (the failure names it).

using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Tests;

public class DocumentLifecycleModelTests
{
    private const int DefaultSeeds = 300;
    private const int Steps = 30;
    private const int BaseSeed = 0x0D0C_11FE;

    static DocumentLifecycleModelTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static int? ReplaySeed =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : null;

    private static IReadOnlyList<int> Seeds()
    {
        if (ReplaySeed is { } one) return [one];
        int total = int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_RUNS"), out var n) && n > 0 ? n : DefaultSeeds;
        return Enumerable.Range(BaseSeed, total).ToList();
    }

    // The transitions the guarantees are about; the runs together must reach every one of them.
    private static readonly string[] Shapes =
    [
        "save refused: encoding", "save refused: lossy", "save wrote a deleted file again",
        "close: save kept it open", "close: don't save", "exit: save all", "exit: don't save", "exit: cancel",
        "exit: second request waited", "exit: os shutdown kept copies", "exit: os shutdown during the question",
        "crash after an autosave", "restore", "restore beside newer edits", "dismiss",
        "external change reloaded", "external change kept unsaved edits", "reload discarding edits",
    ];

    [Fact]
    public void Unsaved_text_is_never_lost_silently()
    {
        var seeds = Seeds();
        var reached = new Dictionary<string, int>(StringComparer.Ordinal);
        bool readOnlyWorks = true;
        // A document's reaction to an external change runs on Avalonia's UI thread; bind it to this run's
        // thread so the reload happens inline instead of queueing forever. (The background parses still
        // post their results to the dispatcher; nothing here depends on them, so they are not run.)
        bool fullRun = seeds.Count >= DefaultSeeds;
        IEnumerable<string> Expected() => readOnlyWorks ? Shapes.Append("save failed: read-only folder") : Shapes;
        bool AllReached() => Expected().All(shape => reached.GetValueOrDefault(shape) >= 3);
        UiThread.Run(() =>
        {
            void RunSeed(int seed)
            {
                using var world = new World(seed, reached);
                readOnlyWorks &= world.ReadOnlyFolderWorks;
                world.Run(Steps);
            }
            foreach (var seed in seeds) RunSeed(seed);
            // How often a seed takes a rare path depends on the platform (file times, read-only folders), so
            // a full run continues with the next seeds, up to three times as many, until every path is reached.
            for (int extra = 0; fullRun && !AllReached() && extra < 2 * seeds.Count; extra++)
                RunSeed(seeds[^1] + 1 + extra);
        });
        if (!fullRun) return;   // a replay or a short run: no reach to check

        foreach (var shape in Expected())
            Assert.True(reached.GetValueOrDefault(shape) >= 3,
                $"the runs reached '{shape}' fewer than 3 times: {string.Join(", ", reached.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))}");
    }

    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private enum Outcome { Ok, Refused, Threw }

    /// <summary>How the read-only folder is locked during a save: not, the folder, or the folder and its files.</summary>
    private enum Lock { None, Folder, FolderAndFiles }

    private enum Op
    {
        Open, Type, Save, CloseTab, Autosave, ExternalWrite, ExternalDelete, DeliverNotice, ReloadDiscarding,
        Exit, Crash, Restart, Restore, Dismiss,
    }

    /// <summary>One running app: the services a process holds, and what the model knows of its state.</summary>
    private sealed class App
    {
        public required DocumentService Docs { get; init; }
        public required CrashRecoveryService Recovery { get; init; }
        public required List<EventHandler<ExternalFileChange>> Watchers { get; init; }
        public ExitFlow Exit { get; set; } = null!;
        public bool Ended { get; set; }
        /// <summary>What the start offered, while the user has not restored or dismissed it.</summary>
        public IReadOnlyList<RecoveredBuffer>? Offer { get; set; }
        /// <summary>Path → text of the last recovery autosave.</summary>
        public Dictionary<string, string> Autosaved { get; } = new(StringComparer.Ordinal);
        /// <summary>Copies of a restored offer (original path, text, the document now holding it), kept with this run until its next autosave.</summary>
        public List<(string Original, string Text, string DocPath)> Settled { get; } = new();
        /// <summary>Open document → the text it last read or saved.</summary>
        public Dictionary<string, string> Saved { get; } = new(StringComparer.Ordinal);
        /// <summary>Open document → read lossily (a save of text still holding U+FFFD is refused).</summary>
        public Dictionary<string, bool> Lossy { get; } = new(StringComparer.Ordinal);
        /// <summary>Files changed on disk whose notice the watcher has not delivered yet.</summary>
        public HashSet<string> Unnoticed { get; } = new(StringComparer.Ordinal);

        public FileDocumentViewModel? Find(string path) =>
            Docs.Documents.FirstOrDefault(d => string.Equals(d.FilePath, path, StringComparison.Ordinal));
    }

    private sealed class World : IDisposable
    {
        private static readonly Encoding Latin2 = Encoding.GetEncoding("iso-8859-2");
        private readonly Random _rnd;
        private readonly Dictionary<string, int> _reached;
        private readonly List<string> _trace = new();
        private int _counter;

        public World(int seed, Dictionary<string, int> reached)
        {
            Seed = seed;
            _rnd = new Random(seed);
            _reached = reached;
            Root = Directory.CreateTempSubdirectory("thide-doc-lifecycle-").FullName;
            Profile = Path.Combine(Root, "profile");
            Project = Directory.CreateDirectory(Path.Combine(Root, "project")).FullName;
            ReadOnlyDir = Directory.CreateDirectory(Path.Combine(Project, "ro")).FullName;
            A = Path.Combine(Project, "a.th");
            B = Path.Combine(Project, "b.th");
            C = Path.Combine(Project, "c.th");
            D = Path.Combine(ReadOnlyDir, "d.th");
            WriteExternally(A, Encoding.UTF8.GetBytes("survey a\n  # invented\nendsurvey\n"));
            WriteExternally(B, Latin2.GetBytes("encoding iso8859-2\nsurvey b\n  # Łódź\nendsurvey\n"));
            // 0xE9 alone is not UTF-8: the file is read lossily, with U+FFFD in its place.
            WriteExternally(C, [.. Encoding.UTF8.GetBytes("survey c\n  # caf"), 0xE9, .. Encoding.UTF8.GetBytes("\nendsurvey\n")]);
            LossyOnDisk[C] = true;
            WriteExternally(D, Encoding.UTF8.GetBytes("survey d\n  # invented\nendsurvey\n"));
            ReadOnlyFolderWorks = ProbeReadOnlyFolder();
        }

        public int Seed { get; }
        public string Root { get; }
        public string Profile { get; }
        public string Project { get; }
        public string ReadOnlyDir { get; }
        public string A { get; }
        public string B { get; }
        public string C { get; }
        public string D { get; }
        public bool ReadOnlyFolderWorks { get; }

        /// <summary>Path → the bytes the last complete write left (null: not there).</summary>
        private Dictionary<string, byte[]?> Disk { get; } = new(StringComparer.Ordinal);
        private Dictionary<string, bool> LossyOnDisk { get; } = new(StringComparer.Ordinal);
        /// <summary>Document path → the last text typed into it, not yet saved or discarded.</summary>
        private Dictionary<string, string> Typed { get; } = new(StringComparer.Ordinal);
        /// <summary>Copies an ended run left that the user has not restored or dismissed (original path, text).</summary>
        private HashSet<(string Path, string Text)> Carried { get; } = new();
        private App? Live { get; set; }

        private string Context => $"seed {Seed} (replay with THIDE_PROPERTY_SEED={Seed}), steps: {string.Join(" → ", _trace)}";
        private void Count(string shape) => _reached[shape] = _reached.GetValueOrDefault(shape) + 1;
        private void Check(bool condition, string what)
        {
            if (!condition) Assert.Fail($"{what}. {Context}");
        }

        public void Run(int steps)
        {
            Start();
            for (int step = 0; step < steps; step++)
            {
                var op = Choose();
                ApplyOp(op);
                CheckInvariants();
            }
            // However the run ended, the next start offers every copy still owed.
            if (Live is not null) { _trace.Add("Crash"); Crash(); }
            _trace.Add("Restart");
            Start();
            Shutdown(Live!, keepRecovery: false);
        }

        // ---- choosing a step --------------------------------------------------------------------

        private Op Choose()
        {
            var w = new List<(Op Op, int Weight)>();
            if (Live is null)
            {
                w.Add((Op.Restart, 8));
                w.Add((Op.ExternalWrite, 1));
                w.Add((Op.ExternalDelete, 1));
            }
            else
            {
                var docs = Live.Docs.Documents;
                w.Add((Op.Open, docs.Count < 4 ? 5 : 1));
                if (docs.Count > 0)
                {
                    w.Add((Op.Type, 8));
                    w.Add((Op.Save, 3));
                    w.Add((Op.CloseTab, 3));
                }
                w.Add((Op.Autosave, 3));
                w.Add((Op.ExternalWrite, 2));
                w.Add((Op.ExternalDelete, 2));
                if (Live.Unnoticed.Count > 0) w.Add((Op.DeliverNotice, 2));
                if (docs.Any(d => d.ExternalChangePending && d.IsDirty)) w.Add((Op.ReloadDiscarding, 2));
                w.Add((Op.Exit, 3));
                w.Add((Op.Crash, 2));
                if (Live.Offer is not null)
                {
                    w.Add((Op.Restore, 5));
                    w.Add((Op.Dismiss, 2));
                }
            }
            int pick = _rnd.Next(w.Sum(x => x.Weight));
            foreach (var (op, weight) in w)
            {
                if (pick < weight) return op;
                pick -= weight;
            }
            throw new InvalidOperationException("unreachable");
        }

        private T Pick<T>(IReadOnlyList<T> items) => items[_rnd.Next(items.Count)];

        private void ApplyOp(Op op)
        {
            _trace.Add(op.ToString());
            switch (op)
            {
                case Op.Restart: Start(); break;
                case Op.Open: Open(); break;
                case Op.Type: Type(); break;
                case Op.Save: Save(); break;
                case Op.CloseTab: CloseTab(); break;
                case Op.Autosave: Autosave(Live!); break;
                case Op.ExternalWrite: ExternalWrite(); break;
                case Op.ExternalDelete: ExternalDelete(); break;
                case Op.DeliverNotice: Deliver(Pick(Live!.Unnoticed.Order(StringComparer.Ordinal).ToList())); break;
                case Op.ReloadDiscarding: ReloadDiscarding(); break;
                case Op.Exit: Exit(); break;
                case Op.Crash: Crash(); break;
                case Op.Restore: Restore(); break;
                case Op.Dismiss: Dismiss(); break;
            }
        }

        // ---- the app's start, the crash and the shutdown ----------------------------------------

        private void Start()
        {
            var watchers = new List<EventHandler<ExternalFileChange>>();
            // The session's file watcher is replaced by the test, which delivers each notice itself.
            var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(
                m => m.ReturnType == typeof(Task) ? Task.CompletedTask
                    : m.ReturnType == typeof(Task<bool>) ? Task.FromResult(false)
                    : null,
                (m, args) => { if (m.Name == "add_ExternalFileChanged") watchers.Add((EventHandler<ExternalFileChange>)args[0]!); });
            var app = new App
            {
                Docs = new DocumentService(new StubResolver(), session),
                Recovery = new CrashRecoveryService(Profile),
                Watchers = watchers,
            };
            app.Exit = new ExitFlow(
                () => app.Docs.Documents.Any(d => d.IsDirty),
                () => SaveBeforeClose.ConfirmAsync(app.Docs.Documents.ToList(), dirty => AnswerExit(app, dirty), app.Docs),
                () => Closing(app, osShutdown: false));
            app.Recovery.MarkRunning();
            var offer = app.Recovery.GetRecoverable();
            var offered = offer.Select(b => (b.OriginalPath, b.Text)).ToHashSet();
            var missing = Carried.Where(c => !offered.Contains(c)).ToList();
            Check(missing.Count == 0, $"the start does not offer {Describe(missing)} (offered {Describe(offered)})");
            if (offer.Count > 0) app.Offer = offer;
            Live = app;
        }

        /// <summary>The main window's Closing handler, replayed: the step ExitFlow chose, and the shutdown it allows.</summary>
        private ExitStep Closing(App app, bool osShutdown)
        {
            var step = app.Exit.OnClosing(osShutdown);
            switch (step)
            {
                case ExitStep.Ask:
                    app.Exit.AskAsync().GetAwaiter().GetResult();
                    break;
                case ExitStep.Exit:
                    Shutdown(app, keepRecovery: false);
                    break;
                case ExitStep.ExitKeepingRecovery:
                    Autosave(app);
                    Shutdown(app, keepRecovery: true);
                    break;
            }
            return step;
        }

        private void Shutdown(App app, bool keepRecovery)
        {
            app.Recovery.MarkCleanShutdown(keepRecovery);
            app.Docs.Close();
            app.Ended = true;
            if (ReferenceEquals(Live, app)) Live = null;
        }

        private void Crash()
        {
            var app = Live!;
            // A crash loses what was typed since the last autosave, and no more.
            bool autosaved = false;
            foreach (var path in Typed.Keys)
                if (app.Autosaved.TryGetValue(path, out var text)) { Carried.Add((path, text)); autosaved = true; }
            foreach (var (original, text, docPath) in app.Settled)
                if (Typed.ContainsKey(docPath)) Carried.Add((original, text));
            if (autosaved) Count("crash after an autosave");
            Typed.Clear();
            SimulateProcessDeath(app.Recovery);
            app.Docs.Close();
            app.Ended = true;
            Live = null;
        }

        /// <summary>The recovery autosave (the 5-second timer, focus loss): every unsaved buffer with a path.</summary>
        private static void Autosave(App app)
        {
            var dirty = app.Docs.Documents.Where(d => d.IsDirty && !string.IsNullOrEmpty(d.FilePath))
                .Select(d => (d.FilePath, d.DocumentText)).ToList();
            app.Recovery.SaveBuffers(dirty);
            app.Autosaved.Clear();
            foreach (var (path, text) in dirty) app.Autosaved[path] = text;
            app.Settled.Clear();
        }

        // ---- editing ----------------------------------------------------------------------------

        private void Open()
        {
            var app = Live!;
            var candidates = Disk.Where(kv => kv.Value is not null && app.Find(kv.Key) is null).Select(kv => kv.Key)
                .Order(StringComparer.Ordinal).ToList();
            if (candidates.Count == 0) return;
            // While an offer waits, often open a file it has a copy of: a restore then meets newer edits.
            var offered = app.Offer?.Select(b => b.OriginalPath).Where(candidates.Contains).Distinct().ToList() ?? [];
            var path = offered.Count > 0 && _rnd.Next(10) < 7 ? Pick(offered) : Pick(candidates);
            app.Docs.OpenFileAsync(path).GetAwaiter().GetResult();
            var doc = app.Find(path);
            Check(doc is not null, $"{Name(path)} did not open");
            var text = DiskText(path)!;
            Check(doc!.DocumentText == text && !doc.IsDirty, $"{Name(path)} opened as \"{Esc(doc.DocumentText)}\" (dirty: {doc.IsDirty}), not as on disk");
            app.Saved[path] = text;
            app.Lossy[path] = LossyOnDisk.GetValueOrDefault(path);
            app.Unnoticed.Remove(path);
        }

        private void Type()
        {
            var app = Live!;
            var docs = app.Docs.Documents.ToList();
            var offered = app.Offer?.Select(b => b.OriginalPath).ToHashSet(StringComparer.Ordinal) ?? [];
            var preferred = docs.Where(d => offered.Contains(d.FilePath)).ToList();
            var doc = preferred.Count > 0 && _rnd.Next(10) < 7 ? Pick(preferred) : Pick(docs);
            var text = NewText(doc.FilePath, typed: true);
            doc.DocumentText = text;
            if (doc.IsDirty) Typed[doc.FilePath] = text;
            else Typed.Remove(doc.FilePath);
        }

        /// <summary>
        /// A complete, invented file for <paramref name="path"/>, unique to this step. It has no trailing
        /// blanks and ends with a newline, so the save's cleanup leaves it alone. A typed b-file sometimes
        /// holds 'ș', which ISO-8859-2 cannot; a typed c-file sometimes keeps the U+FFFD it was read with.
        /// </summary>
        private string NewText(string path, bool typed)
        {
            var marker = $"{(typed ? "typed" : "external")} {++_counter} seed {Seed}";
            return Path.GetFileName(path)[0] switch
            {
                'b' => $"encoding iso8859-2\nsurvey b\n  # {marker} Łódź{(typed && _rnd.Next(3) == 0 ? " ș" : "")}\nendsurvey\n",
                'c' => $"survey c\n  # caf{(typed && _rnd.Next(2) == 0 ? "\uFFFD" : "é")} {marker}\nendsurvey\n",
                var k => $"survey {k}\n  # {marker}\nendsurvey\n",
            };
        }

        /// <summary>
        /// Whether saving <paramref name="doc"/> now writes, is refused before writing (the encoding check
        /// runs first), or fails: in a read-only folder a file that is there is written in place, while a
        /// missing one (no temporary file can be made) or a read-only file cannot be.
        /// </summary>
        private Outcome Predict(App app, FileDocumentViewModel doc, Lock lockKind)
        {
            var text = doc.DocumentText;
            if (app.Lossy.GetValueOrDefault(doc.FilePath) && text.Contains('\uFFFD')) return Outcome.Refused;
            if (text.StartsWith("encoding iso8859-2\n", StringComparison.Ordinal) && text.Contains('ș')) return Outcome.Refused;
            if (lockKind == Lock.None || Path.GetDirectoryName(doc.FilePath) != ReadOnlyDir) return Outcome.Ok;
            if (Disk.GetValueOrDefault(doc.FilePath) is null) return Outcome.Threw;
            return lockKind == Lock.FolderAndFiles ? Outcome.Threw : Outcome.Ok;
        }

        private void CountRefusal(App app, FileDocumentViewModel doc) =>
            Count(app.Lossy.GetValueOrDefault(doc.FilePath) && doc.DocumentText.Contains('\uFFFD') ? "save refused: lossy" : "save refused: encoding");

        /// <summary>A save the model expected to succeed: the file now decodes to the text, which is no longer owed.</summary>
        private void RecordSave(App app, string path, string text)
        {
            if (Disk.GetValueOrDefault(path) is null) Count("save wrote a deleted file again");
            Check(File.Exists(path), $"the save of {Name(path)} reported success but wrote nothing");
            var bytes = File.ReadAllBytes(path);
            Check(SourceFileIo.Read(path, bytes).Text == text, $"{Name(path)} was saved but reads back differently");
            Disk[path] = bytes;
            LossyOnDisk[path] = false;
            app.Saved[path] = text;
            app.Lossy[path] = false;
            Typed.Remove(path);
        }

        /// <summary>For a save in the read-only folder: a third of the time unlocked, a third the folder, a third the folder and its files.</summary>
        private Lock LockFor(FileDocumentViewModel doc) =>
            Path.GetDirectoryName(doc.FilePath) != ReadOnlyDir ? Lock.None : RandomLock();

        private Lock RandomLock() => ReadOnlyFolderWorks ? (Lock)_rnd.Next(3) : Lock.None;

        private void Save()
        {
            var app = Live!;
            var docs = app.Docs.Documents.ToList();
            var dirty = docs.Where(d => d.IsDirty).ToList();
            var doc = dirty.Count > 0 && _rnd.Next(4) > 0 ? Pick(dirty) : Pick(docs);
            var locked = LockFor(doc);
            var text = doc.DocumentText;
            var expected = Predict(app, doc, locked);
            Outcome actual;
            using (ReadOnly(locked))
            {
                try { actual = app.Docs.SaveDocumentAsync(doc).GetAwaiter().GetResult() ? Outcome.Ok : Outcome.Refused; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { actual = Outcome.Threw; }
            }
            Check(actual == expected, $"saving {Name(doc.FilePath)} \"{Esc(text)}\" gave {actual}, expected {expected}");
            switch (actual)
            {
                case Outcome.Ok: RecordSave(app, doc.FilePath, text); break;
                case Outcome.Refused: CountRefusal(app, doc); break;
                case Outcome.Threw: Count("save failed: read-only folder"); break;
            }
        }

        /// <summary>The dock's close guard: Save / Don't save / Cancel for a modified tab, then the close.</summary>
        private void CloseTab()
        {
            var app = Live!;
            var doc = Pick(app.Docs.Documents.ToList());
            var choice = Pick(new[] { SaveBeforeBuildChoice.Save, SaveBeforeBuildChoice.DontSave, SaveBeforeBuildChoice.Cancel });
            bool wasDirty = doc.IsDirty;
            var locked = wasDirty && choice == SaveBeforeBuildChoice.Save ? LockFor(doc) : Lock.None;
            var text = doc.DocumentText;
            var expected = wasDirty && choice == SaveBeforeBuildChoice.Save ? Predict(app, doc, locked) : (Outcome?)null;
            bool asked = false, close;
            using (ReadOnly(locked))
                close = SaveBeforeClose.ConfirmAsync([doc], _ => { asked = true; return Task.FromResult(choice); }, app.Docs)
                    .GetAwaiter().GetResult();
            if (close) app.Docs.CloseDocument(doc);

            Check(asked == wasDirty, $"closing {Name(doc.FilePath)} asked: {asked}, but it was {(wasDirty ? "" : "not ")}modified");
            bool shouldClose = !wasDirty || choice == SaveBeforeBuildChoice.DontSave || expected == Outcome.Ok;
            Check(close == shouldClose, $"closing {Name(doc.FilePath)} with {choice} closed: {close}, expected {shouldClose} ({expected})");
            if (expected == Outcome.Ok) RecordSave(app, doc.FilePath, text);
            else if (expected == Outcome.Refused) { CountRefusal(app, doc); Count("close: save kept it open"); }
            else if (expected == Outcome.Threw) { Count("save failed: read-only folder"); Count("close: save kept it open"); }
            if (wasDirty && choice == SaveBeforeBuildChoice.DontSave) { Typed.Remove(doc.FilePath); Count("close: don't save"); }
            if (close)
            {
                app.Saved.Remove(doc.FilePath);
                app.Lossy.Remove(doc.FilePath);
                app.Unnoticed.Remove(doc.FilePath);
            }
        }

        // ---- the outside world --------------------------------------------------------------------

        private void WriteExternally(string path, byte[] bytes)
        {
            File.WriteAllBytes(path, bytes);
            Disk[path] = bytes;
            LossyOnDisk[path] = false;
        }

        private void ExternalWrite()
        {
            var path = Pick(new[] { A, B, C, D });
            var text = NewText(path, typed: false);
            WriteExternally(path, Path.GetFileName(path)[0] == 'b' ? Latin2.GetBytes(text) : Encoding.UTF8.GetBytes(text));
            Notice(path);
        }

        private void ExternalDelete()
        {
            var existing = Disk.Where(kv => kv.Value is not null).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
            if (existing.Count == 0) return;
            var path = Pick(existing);
            File.Delete(path);
            Disk[path] = null;
            Notice(path);
        }

        /// <summary>The watcher reports a change at once, or later (it lags, or misses it until the next step).</summary>
        private void Notice(string path)
        {
            if (Live?.Find(path) is null) return;
            if (_rnd.Next(5) < 3) Deliver(path);
            else Live.Unnoticed.Add(path);
        }

        private void Deliver(string path)
        {
            var app = Live!;
            app.Unnoticed.Remove(path);
            if (app.Find(path) is not { } doc) return;
            bool deleted = !File.Exists(path), dirty = doc.IsDirty;
            var before = doc.DocumentText;
            foreach (var watcher in app.Watchers) watcher(this, new ExternalFileChange(path, DateTime.UtcNow, deleted));
            if (!deleted && !dirty)
            {
                // A clean document follows the file on disk.
                var text = DiskText(path)!;
                Check(doc.DocumentText == text && !doc.IsDirty, $"{Name(path)} did not reload the external change");
                app.Saved[path] = text;
                app.Lossy[path] = LossyOnDisk.GetValueOrDefault(path);
                Count("external change reloaded");
            }
            else
            {
                Check(doc.DocumentText == before && doc.IsDirty == dirty && doc.ExternalChangePending,
                    $"{Name(path)} changed its buffer or shows no banner after the external {(deleted ? "delete" : "change")}");
                if (dirty && !deleted) Count("external change kept unsaved edits");
            }
        }

        /// <summary>The changed-on-disk banner's Reload over unsaved edits, confirmed: the user discards them.</summary>
        private void ReloadDiscarding()
        {
            var app = Live!;
            var doc = Pick(app.Docs.Documents.Where(d => d.ExternalChangePending && d.IsDirty).ToList());
            doc.ReloadExternalCommand.Execute(null);
            Check(doc.ConfirmDiscardPending, $"Reload over unsaved edits in {Name(doc.FilePath)} did not ask first");
            doc.ConfirmDiscardReloadCommand.Execute(null);
            if (DiskText(doc.FilePath) is { } text)
            {
                Check(doc.DocumentText == text && !doc.IsDirty, $"{Name(doc.FilePath)} did not reload after the user confirmed");
                app.Saved[doc.FilePath] = text;
                app.Lossy[doc.FilePath] = LossyOnDisk.GetValueOrDefault(doc.FilePath);
                Typed.Remove(doc.FilePath);
                Count("reload discarding edits");
            }
            // A file deleted on disk has nothing to reload: the buffer and what it owes stay.
        }

        // ---- exit ---------------------------------------------------------------------------------

        private SaveBeforeBuildChoice _exitChoice;
        private bool _shutdownDuringQuestion, _secondRequestDuringQuestion;
        private Lock _exitLock;
        private List<(FileDocumentViewModel Doc, string Text, Outcome Outcome)>? _exitSaves;

        private void Exit()
        {
            var app = Live!;
            bool osShutdown = _rnd.Next(5) == 0;
            _exitChoice = Pick(new[] { SaveBeforeBuildChoice.Save, SaveBeforeBuildChoice.DontSave, SaveBeforeBuildChoice.Cancel });
            _secondRequestDuringQuestion = _rnd.Next(4) == 0;
            _shutdownDuringQuestion = _rnd.Next(8) == 0;
            _exitLock = RandomLock();
            _exitSaves = null;
            var dirtyBefore = app.Docs.Documents.Where(d => d.IsDirty).Select(d => (d.FilePath, d.DocumentText)).ToList();

            ExitStep step;
            using (ReadOnly(_exitLock)) step = Closing(app, osShutdown);

            switch (step)
            {
                case ExitStep.Exit:
                    Check(dirtyBefore.Count == 0, "the window closed without asking about unsaved documents");
                    Check(Typed.Count == 0, $"the window closed while {Describe(Typed.Select(kv => (kv.Key, kv.Value)))} was owed");
                    break;
                case ExitStep.ExitKeepingRecovery:
                    Check(osShutdown && dirtyBefore.Count > 0, $"kept the recovery copies on a {(osShutdown ? "" : "non-")}OS close with {dirtyBefore.Count} unsaved");
                    KeptAtShutdown(dirtyBefore);
                    break;
                case ExitStep.Ask:
                    Check(!osShutdown && dirtyBefore.Count > 0, "asked on an OS shutdown, or with nothing unsaved");
                    Answered(app, dirtyBefore);
                    break;
                default:
                    Check(false, $"a first close request returned {step}");
                    break;
            }
        }

        /// <summary>The Save all / Don't save / Cancel question; it may be interrupted by a second request or an OS shutdown.</summary>
        private Task<SaveBeforeBuildChoice> AnswerExit(App app, IReadOnlyList<FileDocumentViewModel> dirty)
        {
            if (_secondRequestDuringQuestion)
            {
                Check(app.Exit.OnClosing(osShutdown: false) == ExitStep.Wait, "a second close request while asked did not wait for the answer");
                Count("exit: second request waited");
            }
            if (_shutdownDuringQuestion)
            {
                // The OS ends the session while the question is up: the buffers are kept, the dialog goes.
                var unsaved = app.Docs.Documents.Where(d => d.IsDirty).Select(d => (d.FilePath, d.DocumentText)).ToList();
                Check(Closing(app, osShutdown: true) == ExitStep.ExitKeepingRecovery, "an OS shutdown during the question did not keep the copies");
                KeptAtShutdown(unsaved);
                Count("exit: os shutdown during the question");
                return Task.FromResult(SaveBeforeBuildChoice.Cancel);
            }
            if (_exitChoice == SaveBeforeBuildChoice.Save)
            {
                // SaveBeforeClose saves them in order and stops at the first that throws.
                _exitSaves = new();
                foreach (var doc in dirty)
                {
                    var outcome = Predict(app, doc, _exitLock);
                    _exitSaves.Add((doc, doc.DocumentText, outcome));
                    if (outcome == Outcome.Threw) break;
                }
            }
            return Task.FromResult(_exitChoice);
        }

        private void Answered(App app, List<(string Path, string Text)> dirtyBefore)
        {
            if (_shutdownDuringQuestion)
            {
                Check(app.Ended, "the app kept running after the OS shutdown");
                return;
            }
            switch (_exitChoice)
            {
                case SaveBeforeBuildChoice.Cancel:
                    Check(!app.Ended, "Cancel closed the window");
                    Count("exit: cancel");
                    break;
                case SaveBeforeBuildChoice.DontSave:
                    Check(app.Ended, "Don't save did not close the window");
                    foreach (var (path, _) in dirtyBefore) Typed.Remove(path);
                    Check(Typed.Count == 0, $"Don't save closed while {Describe(Typed.Select(kv => (kv.Key, kv.Value)))} was owed");
                    Count("exit: don't save");
                    break;
                case SaveBeforeBuildChoice.Save:
                    foreach (var (doc, text, outcome) in _exitSaves!)
                    {
                        if (outcome == Outcome.Ok) RecordSave(app, doc.FilePath, text);
                        else if (outcome == Outcome.Refused) CountRefusal(app, doc);
                        else Count("save failed: read-only folder");
                    }
                    bool allSaved = _exitSaves.Count == dirtyBefore.Count && _exitSaves.All(s => s.Outcome == Outcome.Ok);
                    Check(app.Ended == allSaved, $"Save all {(app.Ended ? "closed" : "kept open")} the window, expected {(allSaved ? "closed" : "open")}");
                    if (allSaved) Check(Typed.Count == 0, $"Save all closed while {Describe(Typed.Select(kv => (kv.Key, kv.Value)))} was owed");
                    Count("exit: save all");
                    break;
            }
        }

        /// <summary>An exit that could not ask: every unsaved buffer was written as a recovery copy and kept.</summary>
        private void KeptAtShutdown(List<(string Path, string Text)> unsaved)
        {
            foreach (var copy in unsaved) Carried.Add(copy);
            Typed.Clear();
            Count("exit: os shutdown kept copies");
        }

        // ---- the offer --------------------------------------------------------------------------

        /// <summary>The main window's RecoverBuffersAsync: reopen every offered buffer, then settle the offer.</summary>
        private void Restore()
        {
            var app = Live!;
            var offer = app.Offer!;
            var dirtyBefore = app.Docs.Documents.Where(d => d.IsDirty).Select(d => (Doc: d, Text: d.DocumentText)).ToList();
            var restored = new List<(FileDocumentViewModel Doc, string Text)>();
            foreach (var b in offer)
            {
                var open = app.Find(b.OriginalPath);
                bool existed = File.Exists(b.OriginalPath);
                if (open is { IsDirty: true } && open.DocumentText != b.Text) Count("restore beside newer edits");
                var doc = app.Docs.OpenRecoveredAsync(b.OriginalPath, b.Text).GetAwaiter().GetResult();
                Check(doc.DocumentText == b.Text, $"the restore of {Name(b.OriginalPath)} did not open its text");
                if (!app.Saved.ContainsKey(doc.FilePath))
                {
                    // A new tab: read from the file, or (gone, or beside newer edits) one whose saved state is empty.
                    bool fromDisk = open is null && existed && doc.FilePath == b.OriginalPath;
                    app.Saved[doc.FilePath] = fromDisk ? DiskText(doc.FilePath)! : string.Empty;
                    app.Lossy[doc.FilePath] = fromDisk && LossyOnDisk.GetValueOrDefault(doc.FilePath);
                }
                restored.Add((doc, b.Text));
            }
            app.Recovery.ClearRecovery();   // every buffer reopened: OpenRecoveredAsync does not fail

            foreach (var (doc, text) in dirtyBefore)
                Check(app.Docs.Documents.Contains(doc) && doc.DocumentText == text, $"the restore replaced newer unsaved edits in {Name(doc.FilePath)}");
            foreach (var (doc, text) in restored)
            {
                Check(app.Docs.Documents.Contains(doc) && doc.DocumentText == text, $"a restored buffer for {Name(doc.FilePath)} was replaced by a later one");
                if (doc.IsDirty) Typed[doc.FilePath] = text;
            }
            foreach (var (b, (doc, _)) in offer.Zip(restored)) app.Settled.Add((b.OriginalPath, b.Text, doc.FilePath));
            Settle(app);
            Count("restore");
        }

        private void Dismiss()
        {
            var app = Live!;
            app.Recovery.ClearRecovery();
            Settle(app);
            Count("dismiss");
        }

        private void Settle(App app)
        {
            foreach (var b in app.Offer!) Carried.Remove((b.OriginalPath, b.Text));
            app.Offer = null;
        }

        // ---- the guarantees ---------------------------------------------------------------------

        private void CheckInvariants()
        {
            // 2. The disk holds the last complete write, and nothing else appeared in the project.
            foreach (var (path, bytes) in Disk)
            {
                var actual = File.Exists(path) ? File.ReadAllBytes(path) : null;
                Check(actual is null == bytes is null && (actual is null || actual.AsSpan().SequenceEqual(bytes)),
                    $"{Name(path)} on disk is {(actual is null ? "gone" : $"\"{Esc(Decode(path, actual))}\"")}, " +
                    $"expected {(bytes is null ? "gone" : $"\"{Esc(Decode(path, bytes))}\"")}");
            }
            var stray = Directory.GetFiles(Project, "*", SearchOption.AllDirectories)
                .Where(f => Disk.GetValueOrDefault(f) is null).ToList();
            Check(stray.Count == 0, $"unexpected files in the project: {string.Join(", ", stray.Select(Name))}");

            if (Live is { } app)
            {
                // 3. Unsaved exactly when the buffer differs from what it last read or saved.
                foreach (var doc in app.Docs.Documents)
                {
                    Check(app.Saved.TryGetValue(doc.FilePath, out var saved), $"{Name(doc.FilePath)} is open but the model never saw it open");
                    Check(doc.IsDirty == (doc.DocumentText != saved),
                        $"{Name(doc.FilePath)} is {(doc.IsDirty ? "" : "not ")}marked unsaved, buffer \"{Esc(doc.DocumentText)}\", last read or saved \"{Esc(saved!)}\"");
                }
            }

            // 1. No silent loss.
            foreach (var (path, text) in Typed)
            {
                bool inBuffer = Live?.Find(path)?.DocumentText == text;
                Check(inBuffer || DiskText(path) == text, $"the text last typed into {Name(path)}, \"{Esc(text)}\", is neither in its buffer nor on disk");
            }
            if (Live is { } live && Carried.Count > 0)
            {
                var offered = live.Offer is null ? [] : live.Recovery.GetRecoverable().Select(b => (b.OriginalPath, b.Text)).ToHashSet();
                foreach (var (path, text) in Carried)
                {
                    bool open = live.Docs.Documents.Any(d => d.DocumentText == text);
                    Check(open || DiskText(path) == text || offered.Contains((path, text)),
                        $"the recovered text for {Name(path)}, \"{Esc(text)}\", is not open, not on disk and no longer offered");
                }
            }
        }

        // ---- helpers ------------------------------------------------------------------------------

        private string? DiskText(string path) =>
            Disk.GetValueOrDefault(path) is { } bytes ? SourceFileIo.Read(path, bytes).Text : null;

        private static string Decode(string path, byte[] bytes) => SourceFileIo.Read(path, bytes).Text;

        private string Name(string path) => Path.GetRelativePath(Project, path).Replace('\\', '/');

        private static string Esc(string s) => s.Replace("\n", "\\n");

        private string Describe(IEnumerable<(string Path, string Text)> copies) =>
            "[" + string.Join(", ", copies.Select(c => $"{Name(c.Path)} \"{Esc(c.Text)}\"")) + "]";

        /// <summary>Makes the read-only folder (and its files) unwritable for the duration (POSIX; a no-op for <see cref="Lock.None"/>).</summary>
        private IDisposable ReadOnly(Lock lockKind)
        {
            if (lockKind == Lock.None || OperatingSystem.IsWindows()) return new Unlock(null, []);
            var files = lockKind == Lock.FolderAndFiles ? Directory.GetFiles(ReadOnlyDir) : [];
            foreach (var f in files) File.SetUnixFileMode(f, UnixFileMode.UserRead);
            File.SetUnixFileMode(ReadOnlyDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            return new Unlock(ReadOnlyDir, files);
        }

        private sealed class Unlock(string? dir, string[] files) : IDisposable
        {
            public void Dispose()
            {
                if (dir is null || OperatingSystem.IsWindows()) return;
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                foreach (var f in files)
                    if (File.Exists(f)) File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        /// <summary>True when a read-only folder really refuses a new file here (not on Windows, not as root).</summary>
        private bool ProbeReadOnlyFolder()
        {
            if (OperatingSystem.IsWindows()) return false;
            using (ReadOnly(Lock.Folder))
            {
                try { File.WriteAllText(Path.Combine(ReadOnlyDir, "probe"), ""); }
                catch (UnauthorizedAccessException) { return true; }
                catch (IOException) { return true; }
            }
            File.Delete(Path.Combine(ReadOnlyDir, "probe"));
            return false;
        }

        public void Dispose()
        {
            if (Live is { } app) { SimulateProcessDeath(app.Recovery); app.Docs.Close(); }
            try
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(ReadOnlyDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Directory.Delete(Root, recursive: true);
            }
            catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// What the OS does when the process dies: the run lock it held open is released, and the lock
    /// file and the recovery copies stay on disk.
    /// </summary>
    private static void SimulateProcessDeath(CrashRecoveryService recovery)
    {
        var field = typeof(CrashRecoveryService).GetField("_lock", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CrashRecoveryService no longer holds its run lock in '_lock'; update the crash simulation.");
        (field.GetValue(recovery) as IDisposable)?.Dispose();
        field.SetValue(recovery, null);
    }
}
