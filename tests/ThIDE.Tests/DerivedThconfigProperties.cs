// The derived thconfig a single-target build, Quick Export or a partial batch hands to Therion, over
// generated thconfigs: UTF-8 with and without a BOM, Latin-1 and Latin-2 behind an `encoding` line;
// LF or CRLF; `source` lines bare, quoted with spaces, `\`-continued and with Windows separators;
// several exports (some continued over two lines), `system` lines, a layout block and non-ASCII
// comments.
//
// What must hold:
// - the copy is the transform of the source text, in the source's encoding and BOM;
// - it loads exactly the files the source thconfig loads, and keeps every non-export line as it was;
// - it builds exactly the exports asked for;
// - with unsaved edits in the editor (lines added or removed above an export, exports reordered, an
//   export's output changed in place), the build saves first and builds the chosen export of the
//   saved text, the same one the user picked from the list made before the edit.
//
// Seeds are fixed (THIDE_PROPERTY_SEED overrides the base); a failure names the seed.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using Therion.Build;
using Therion.Build.Automation;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class DerivedThconfigProperties
{
    private const int DefaultSeed = 0x0D3_C0F0;
    private const int WriteCases = 60;
    private const int BuildCases = 15;

    private static int BaseSeed =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : DefaultSeed;

    static DerivedThconfigProperties() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // ---- generated thconfig ------------------------------------------------------------------

    private static readonly (string? Line, int CodePage, bool Bom, string NonAscii)[] Encodings =
    [
        (null, 65001, false, "Peșteră →"),
        ("encoding utf-8", 65001, true, "Peșteră"),
        ("encoding iso8859-1", 28591, false, "Grotte Café"),
        ("encoding iso8859-2", 28592, false, "Jaskinia łódź"),
    ];

    /// <summary>One thconfig item: an export (with an id the edits keep) or any other line run.</summary>
    private sealed class Item
    {
        public int ExportId = -1;
        public string Kind = "";
        public string Output = "";
        public List<string> Lines = [];
        public bool IsExport => ExportId >= 0;

        public static Item Export(int id, string kind, string output) =>
            new() { ExportId = id, Kind = kind, Output = output, Lines = ExportLines(kind, output) };

        public static List<string> ExportLines(string kind, string output) => kind switch
        {
            "map" => [$"export map -o {output}"],
            "model" => [$"export model -fmt loch -o {output}"],
            "continued" => ["export map -proj extended \\", $"  -o {output}"],
            _ => [$"export map -layout L1 -o \"{output}\""],
        };

        public void SetOutput(string output) { Output = output; Lines = ExportLines(Kind, output); }
    }

    private sealed class Cfg
    {
        public required (string? Line, int CodePage, bool Bom, string NonAscii) Enc;
        public required string Eol;
        public required List<Item> Items;
        public Encoding Encoding => Encoding.GetEncoding(Enc.CodePage);

        public string Text()
        {
            var sb = new StringBuilder();
            if (Enc.Line is not null) sb.Append(Enc.Line).Append(Eol);
            foreach (var item in Items)
                foreach (var line in item.Lines) sb.Append(line).Append(Eol);
            return sb.ToString();
        }

        public byte[] Bytes() => [.. Enc.Bom ? Encoding.UTF8.GetPreamble() : [], .. Encoding.GetBytes(Text())];

        public IEnumerable<Item> Exports => Items.Where(i => i.IsExport);
    }

    private static Cfg Generate(Random rng, string dir)
    {
        var enc = Encodings[rng.Next(Encodings.Length)];
        var items = new List<Item>();

        // Sources, each to a file written here so the workspace can load it.
        var sources = new (string Line, string? Continued, string File)[]
        {
            ("source cave.th", null, "cave.th"),
            ("source \"my cave/deep one.th\"", null, "my cave/deep one.th"),
            ("source \\", "  cont.th", "cont.th"),
            ("source sub\\win.th", null, "sub/win.th"),
        };
        foreach (var (line, continued, file) in sources.OrderBy(_ => rng.Next()).Take(rng.Next(1, 5)))
        {
            items.Add(new Item { Lines = continued is null ? [line] : [line, continued] });
            var full = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var survey = "s_" + new string(file.Where(char.IsLetter).ToArray());
            File.WriteAllText(full, $"survey {survey}\n  centreline\n    1 2 5 0 0\n  endcentreline\nendsurvey\n");
        }

        string[] kinds = ["map", "model", "continued", "quoted"];
        int exports = rng.Next(2, 5);
        for (int id = 0; id < exports; id++)
        {
            var kind = kinds[rng.Next(kinds.Length)];
            var output = kind switch
            {
                "model" => $"cave{id}.lox",
                "quoted" => $"out dir/p{id}.pdf",
                _ => $"plan{id}.pdf",
            };
            items.Add(Item.Export(id, kind, output));
        }
        if (rng.Next(2) == 0) items.Add(new Item { Lines = ["system \"echo built\""] });
        if (rng.Next(2) == 0) items.Add(new Item { Lines = ["layout L1", "  scale 1 500", "endlayout"] });
        for (int k = rng.Next(0, 3); k > 0; k--) items.Add(new Item { Lines = [$"# {enc.NonAscii} {k}"] });

        // Any order, except that a layout must come before the export that names it.
        items = [.. items.OrderBy(_ => rng.Next())];
        var layout = items.FindIndex(i => i.Lines[0] == "layout L1");
        if (layout >= 0)
        {
            var block = items[layout];
            items.RemoveAt(layout);
            items.Insert(0, block);
        }
        return new Cfg { Enc = enc, Eol = rng.Next(2) == 0 ? "\n" : "\r\n", Items = items };
    }

    private static string Seeded(int seed, string message) => $"seed {seed} (base {BaseSeed}): {message}";

    // ---- the copy itself -----------------------------------------------------------------------

    [Fact]
    public async Task The_copy_is_the_transform_of_the_source_in_its_encoding_and_loads_the_same_files()
    {
        var failures = new List<string>();
        for (int i = 0; i < WriteCases; i++)
        {
            int seed = BaseSeed + i;
            var dir = Directory.CreateTempSubdirectory("thide-derived-prop-").FullName;
            try
            {
                var rng = new Random(seed);
                var cfg = Generate(rng, dir);
                var source = Path.Combine(dir, "main.thconfig");
                File.WriteAllBytes(source, cfg.Bytes());
                var read = SourceFileIo.Read(source);
                var exports = ThconfigExportEditor.ParseExports(read.Text);
                if (exports.Count != cfg.Exports.Count())
                {
                    failures.Add(Seeded(seed, $"generator: {exports.Count} exports parsed, {cfg.Exports.Count()} written"));
                    continue;
                }
                var sourceFiles = await LoadedBy(source);

                var keepOne = exports[rng.Next(exports.Count)];
                var keepSome = exports.Where(_ => rng.Next(2) == 0).ToList();
                var transforms = new (string Name, Func<string, string> Apply, IEnumerable<string?> Kept)[]
                {
                    ("single target", t => ThconfigExportEditor.IsolateExport(t, keepOne), [keepOne.Output]),
                    ("partial batch", t => ThconfigExportEditor.IsolateExports(t, keepSome), keepSome.Select(e => e.Output)),
                    ("quick export", t => ThconfigExportEditor.ComposeExport(t, "export model -fmt loch -o quick.lox"), ["quick.lox"]),
                };
                foreach (var (name, apply, kept) in transforms)
                {
                    var temp = Path.Combine(dir, ".thide-target.thconfig");
                    DerivedThconfig.Write(source, temp, apply);
                    var bytes = File.ReadAllBytes(temp);
                    var want = apply(read.Text);
                    byte[] wantBytes = [.. read.Bom, .. read.Encoding.GetBytes(want)];
                    var derived = SourceFileIo.Read(temp, bytes);
                    if (derived.Text != want) failures.Add(Seeded(seed, $"{name}: the copy is not the transform of the source text"));
                    else if (!bytes.AsSpan().SequenceEqual(wantBytes)) failures.Add(Seeded(seed, $"{name}: the copy is not in the source's encoding ({read.Encoding.WebName}, BOM {read.Bom.Length})"));

                    var built = ThconfigExportEditor.ParseExports(derived.Text).Select(e => e.Output).OrderBy(o => o, StringComparer.Ordinal);
                    if (!built.SequenceEqual(kept.OrderBy(o => o, StringComparer.Ordinal)))
                        failures.Add(Seeded(seed, $"{name}: builds [{string.Join(", ", built)}], asked for [{string.Join(", ", kept)}]"));

                    var srcLines = read.Text.Split('\n');
                    var outLines = derived.Text.Split('\n');
                    var exportLines = exports.SelectMany(e => Enumerable.Range(e.StartLine, e.EndLine - e.StartLine + 1)).ToHashSet();
                    for (int ln = 1; ln <= srcLines.Length && ln <= outLines.Length; ln++)
                        if (!exportLines.Contains(ln) && srcLines[ln - 1].TrimEnd('\r').Length > 0
                            && outLines[ln - 1].TrimEnd('\r') != srcLines[ln - 1].TrimEnd('\r'))
                        {
                            failures.Add(Seeded(seed, $"{name}: line {ln} ('{srcLines[ln - 1].TrimEnd('\r')}') is not kept as it was"));
                            break;
                        }

                    var tempFiles = await LoadedBy(temp);
                    if (!tempFiles.SetEquals(sourceFiles))
                        failures.Add(Seeded(seed, $"{name}: the copy loads [{string.Join(", ", tempFiles.Select(f => Path.GetRelativePath(dir, f)))}], the source [{string.Join(", ", sourceFiles.Select(f => Path.GetRelativePath(dir, f)))}]"));
                    File.Delete(temp);
                }
            }
            catch (Exception ex) { failures.Add(Seeded(seed, "threw " + ex)); }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(8)));
    }

    // The files a workspace loaded from the thconfig, other than the thconfig itself.
    private static async Task<HashSet<string>> LoadedBy(string thconfig)
    {
        await using var ws = new TherionWorkspace(options: new WorkspaceOptions { DisableDiskCache = true });
        await ws.LoadAsync(thconfig);
        return ws.LoadedFiles.Select(Path.GetFullPath).Where(f => f != Path.GetFullPath(thconfig)).ToHashSet(StringComparer.Ordinal);
    }

    // ---- the builds that make one, with unsaved edits ----------------------------------------

    public enum Path3 { SingleTarget, QuickExport, PartialBatch }

    [Theory]
    [InlineData(Path3.SingleTarget)]
    [InlineData(Path3.QuickExport)]
    [InlineData(Path3.PartialBatch)]
    public async Task A_build_with_unsaved_edits_saves_first_and_builds_the_chosen_exports_of_the_saved_text(Path3 path)
    {
        var failures = new List<string>();
        for (int i = 0; i < BuildCases; i++)
        {
            int seed = BaseSeed + 1000 * ((int)path + 1) + i;
            var dir = Directory.CreateTempSubdirectory("thide-derived-build-").FullName;
            try
            {
                var message = await RunBuildCase(path, seed, dir);
                if (message is not null) failures.Add(Seeded(seed, $"{path}: {message}"));
            }
            catch (Exception ex) { failures.Add(Seeded(seed, $"{path}: threw {ex}")); }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(8)));
    }

    private static async Task<string?> RunBuildCase(Path3 path, int seed, string dir)
    {
        var rng = new Random(seed);
        var cfg = Generate(rng, dir);
        var file = Path.Combine(dir, "main.thconfig");
        File.WriteAllBytes(file, cfg.Bytes());
        var exports = cfg.Exports.ToList();

        // What the user picks, from the list made from the file before the edit.
        var picked = path == Path3.PartialBatch
            ? exports.Where(_ => rng.Next(2) == 0).ToList()
            : [exports[rng.Next(exports.Count)]];
        if (path == Path3.PartialBatch && (picked.Count == 0 || picked.Count == exports.Count))
            picked = [exports[0]];
        var pickedOutputs = picked.Select(e => e.Output).ToList();

        // The batch window and the target list are made now, from the file as saved.
        var docs = new ObservableCollection<FileDocumentViewModel>();
        var compiler = new RecordingCompiler();
        BuildViewModel? build = null;
        BatchBuildViewModel? batch = null;
        var batchBuilt = new List<byte[]>();
        if (path == Path3.PartialBatch)
        {
            var env = new BatchBuildEnvironment([file], dir,
                (entry, temp, label) =>
                {
                    batchBuilt.Add(File.ReadAllBytes(entry));
                    if (temp is not null) File.Delete(temp);
                    return Task.FromResult<BuildOutcome?>(new BuildOutcome(entry, true, false, 0, 0, 0, [], null, TimeSpan.Zero, label));
                },
                () => { }, null, null,
                SaveFirst: async p =>
                {
                    foreach (var d in docs.Where(d => d.IsDirty && d.FilePath == p)) Save(d);
                    await Task.Yield();
                    return true;
                });
            batch = new BatchBuildViewModel(env);
            foreach (var row in batch.Configs[0].Targets)
                row.IsChecked = pickedOutputs.Contains(row.Target.Output!);
        }
        else
        {
            build = new BuildViewModel(compiler, new CompileGate(), new NoShell(), new NoTools(), DocumentsOf(docs), new NoCache(),
                SessionFor(file), new FakeSettings(AppSettings.Default with { AutoSaveBeforeCompile = true }));
        }

        // Unsaved edits: either lines move (comments added, a line removed, exports reordered), or the
        // chosen exports' outputs are changed where they stand.
        bool inPlace = rng.Next(3) == 0;
        if (inPlace)
        {
            foreach (var e in picked) e.SetOutput("edited_" + e.Output);
        }
        else
        {
            for (int n = rng.Next(1, 4); n > 0; n--)
            {
                switch (rng.Next(3))
                {
                    case 0:
                        cfg.Items.Insert(rng.Next(cfg.Items.Count + 1), new Item { Lines = [$"# added {cfg.Enc.NonAscii}"] });
                        break;
                    case 1:
                        var others = cfg.Items.Where(it => !it.IsExport && it.Lines[0] != "layout L1").ToList();
                        if (others.Count > 0) cfg.Items.Remove(others[rng.Next(others.Count)]);
                        break;
                    default:
                        var ex = cfg.Items.Select((it, k) => (it, k)).Where(p => p.it.IsExport).Select(p => p.k).ToList();
                        int a = ex[rng.Next(ex.Count)], b = ex[rng.Next(ex.Count)];
                        (cfg.Items[a], cfg.Items[b]) = (cfg.Items[b], cfg.Items[a]);
                        break;
                }
            }
        }
        var edited = cfg.Text();
        var doc = new FileDocumentViewModel(file, SourceFileIo.Read(file).Text, new MeasurementsViewModel());
        doc.DocumentText = edited;
        if (!doc.IsDirty) return null;   // the edits cancelled out
        docs.Add(doc);

        byte[] builtBytes;
        switch (path)
        {
            case Path3.SingleTarget:
            {
                var target = build!.ExportTargets.SingleOrDefault(t => t.Title.EndsWith("→  " + pickedOutputs[0], StringComparison.Ordinal));
                if (target is null) return $"no target listed for {pickedOutputs[0]}";
                var finished = new TaskCompletionSource<BuildOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                build.BuildFinished += (_, o) => finished.TrySetResult(o);
                target.BuildCommand.Execute(null);
                if (await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(10))) != finished.Task)
                    return $"picking {pickedOutputs[0]} built nothing (status: {build.Status})";
                builtBytes = compiler.EntryBytes;
                break;
            }
            case Path3.QuickExport:
                await build!.RunQuickExportAsync("export model -fmt loch -o quick.lox", "quick.lox");
                if (compiler.Entry is null) return $"Quick Export built nothing (status: {build.Status})";
                builtBytes = compiler.EntryBytes;
                break;
            default:
                await batch!.RunAsync();
                if (batchBuilt.Count != 1) return $"the batch built {batchBuilt.Count} entries (status: {batch.Status})";
                builtBytes = batchBuilt[0];
                break;
        }

        // Saved first, in the file's own encoding.
        var saved = SourceFileIo.Read(file);
        if (saved.Text != edited) return "the edited buffer was not saved before the build";
        if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(cfg.Bytes())) return "the save changed the file's encoding or BOM";

        // Built from the saved text, keeping exactly what was picked.
        var builtFile = SourceFileIo.Read(file, builtBytes);
        if (builtFile.Encoding.CodePage != saved.Encoding.CodePage || !builtFile.Bom.AsSpan().SequenceEqual(saved.Bom))
            return "the derived thconfig is not in the saved file's encoding";
        var now = ThconfigExportEditor.ParseExports(saved.Text);
        string want = path switch
        {
            Path3.QuickExport => ThconfigExportEditor.ComposeExport(saved.Text, "export model -fmt loch -o quick.lox"),
            _ => ThconfigExportEditor.IsolateExports(saved.Text, now.Where(e => picked.Any(p => p.Output == e.Output)).ToList()),
        };
        if (builtFile.Text != want)
        {
            var kept = ThconfigExportEditor.ParseExports(builtFile.Text).Select(e => e.Output);
            return $"picked [{string.Join(", ", picked.Select(p => p.Output))}] after {(inPlace ? "editing it in place" : "moving lines")}, built [{string.Join(", ", kept)}]";
        }
        return null;
    }

    [Fact]
    public async Task A_target_the_saved_text_no_longer_names_is_refused_rather_than_built_as_another()
    {
        var dir = Directory.CreateTempSubdirectory("thide-derived-gone-").FullName;
        try
        {
            var file = Path.Combine(dir, "main.thconfig");
            File.WriteAllText(file, "source cave.th\nexport map -o plan.pdf\nexport model -o cave.lox\n");
            var docs = new ObservableCollection<FileDocumentViewModel>();
            var compiler = new RecordingCompiler();
            var build = new BuildViewModel(compiler, new CompileGate(), new NoShell(), new NoTools(), DocumentsOf(docs), new NoCache(),
                SessionFor(file), new FakeSettings(AppSettings.Default with { AutoSaveBeforeCompile = true }));
            var map = Assert.Single(build.ExportTargets, t => t.Title.EndsWith("→  plan.pdf", StringComparison.Ordinal));
            // Moved down a line and given another output: no longer the export that was listed.
            var doc = new FileDocumentViewModel(file, File.ReadAllText(file), new MeasurementsViewModel());
            doc.DocumentText = "source cave.th\n# a note\nexport map -o plan2.pdf\nexport model -o cave.lox\n";
            docs.Add(doc);

            map.BuildCommand.Execute(null);
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!build.Status.Contains("plan.pdf", StringComparison.Ordinal) && DateTime.UtcNow < until) await Task.Delay(20);

            Assert.Contains("export map", build.Status);
            Assert.Null(compiler.Entry);
            Assert.False(File.Exists(Path.Combine(dir, ".thide-target.thconfig")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    private static void Save(FileDocumentViewModel doc)
    {
        // As the editor saves: in the encoding the file is in.
        var (bytes, format) = SourceFileIo.EncodeForSave(doc.FilePath, doc.DocumentText, doc.Format);
        File.WriteAllBytes(doc.FilePath, bytes);
        doc.Format = format;
        doc.SetText(doc.DocumentText, reparse: false);
    }

    // ---- fakes -----------------------------------------------------------------------------

    /// <summary>Records the bytes of the entry file at the moment the "compiler" runs (the temp is deleted afterwards).</summary>
    private sealed class RecordingCompiler : ITherionCompiler
    {
        public string? Entry;
        public byte[] EntryBytes = [];
        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            Entry = entryPointPath;
            EntryBytes = File.ReadAllBytes(entryPointPath);
            return new(new CompileResult(0, ImmutableArray<Therion.Core.Diagnostic>.Empty, ImmutableArray<OutputArtifact>.Empty));
        }
    }

    private sealed class NoShell : IShellOpener
    {
        public bool Open(string path) => true;
        public bool RevealInFileManager(string path) => true;
    }

    private sealed class NoTools : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) => new((ToolInfo?)null);
    }

    private sealed class NoCache : IOutputArtifactCache
    {
        public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion) => ImmutableArray<OutputArtifact>.Empty;
        public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) { }
        public void Clear(string entryPointPath, string therionVersion) { }
    }

    private static IWorkspaceSession SessionFor(string cfg) =>
        WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name switch
        {
            "get_ActiveThconfig" => new ThconfigCandidate(cfg, Path.GetFileName(cfg), false),
            "get_RootPath" => Path.GetDirectoryName(cfg),
            "get_Candidates" => new List<ThconfigCandidate> { new(cfg, Path.GetFileName(cfg), false) },
            "Covers" => true,
            _ => null,
        });

    private static IDocumentService DocumentsOf(ObservableCollection<FileDocumentViewModel> docs) =>
        WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Documents" => docs,
            "get_CurrentText" => string.Empty,
            "get_CurrentDiagnostics" => ImmutableArray<Therion.Core.Diagnostic>.Empty,
            "SaveDocumentAsync" => Task.FromResult(true),
            _ => null,
        }, (m, args) =>
        {
            if (m.Name == "SaveDocumentAsync") Save((FileDocumentViewModel)args[0]!);
        });
}
