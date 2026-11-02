// Builds that go through a derived (temporary) thconfig: a single export target, Quick Export and a
// partial batch. The derived copy must be made from what the user is about to build (the saved
// editor text, in the file's own encoding), and a failure to make it must be reported, not thrown.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using Therion.Build;
using Therion.Build.Automation;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using Therion.Workspace;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class DerivedThconfigBuildTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-derived-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

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

    /// <summary>A session whose active thconfig is <paramref name="cfg"/>; it reports every file as covered
    /// (the only file the tests open is that thconfig, which the real session covers too).</summary>
    private static IWorkspaceSession SessionFor(string cfg) =>
        WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name switch
        {
            "get_ActiveThconfig" => new ThconfigCandidate(cfg, Path.GetFileName(cfg), false),
            "get_RootPath" => Path.GetDirectoryName(cfg),
            "get_Candidates" => new List<ThconfigCandidate> { new(cfg, Path.GetFileName(cfg), false) },
            "Covers" => true,
            _ => null,
        });

    /// <summary>Open documents; saving one writes its text to disk the way the real service does.</summary>
    private static IDocumentService DocumentsOf(ObservableCollection<FileDocumentViewModel> docs) =>
        WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Documents" => docs,
            "get_CurrentText" => string.Empty,
            "get_CurrentDiagnostics" => ImmutableArray<Therion.Core.Diagnostic>.Empty,
            "SaveDocumentAsync" => Task.CompletedTask,
            _ => null,
        }, (m, args) =>
        {
            if (m.Name != "SaveDocumentAsync") return;
            var doc = (FileDocumentViewModel)args[0]!;
            File.WriteAllText(doc.FilePath, doc.DocumentText);
            doc.SetText(doc.DocumentText, reparse: false);
        });

    private (BuildViewModel Vm, RecordingCompiler Compiler) MakeBuild(string cfg, ObservableCollection<FileDocumentViewModel> docs, bool autoSave = true)
    {
        var compiler = new RecordingCompiler();
        var settings = new FakeSettings(AppSettings.Default with { AutoSaveBeforeCompile = autoSave });
        var vm = new BuildViewModel(compiler, new CompileGate(), new NoShell(), new NoTools(), DocumentsOf(docs), new NoCache(),
            SessionFor(cfg), settings);
        return (vm, compiler);
    }

    private static string DecodeAsTherionWould(byte[] bytes) => EncodingResolver.Decode(bytes);

    // ---- unsaved thconfig edits -------------------------------------------------------------

    [Fact]
    public async Task Quick_export_builds_the_thconfig_text_it_saved()
    {
        var cfg = Path.Combine(_root, "main.thconfig");
        File.WriteAllText(cfg, "source old.th\nexport map -o plan.pdf\n");
        var doc = new FileDocumentViewModel(cfg, File.ReadAllText(cfg), new MeasurementsViewModel());
        doc.DocumentText = "source new.th\nexport map -o plan.pdf\n";    // edited, not yet saved
        Assert.True(doc.IsDirty);
        var (vm, compiler) = MakeBuild(cfg, [doc]);

        await vm.RunQuickExportAsync("export model -fmt loch -o quick.lox", "quick.lox");

        // The build saved the edited thconfig first ...
        Assert.Contains("source new.th", File.ReadAllText(cfg));
        Assert.False(doc.IsDirty);
        // ... so the derived thconfig it compiled must carry the same edit.
        Assert.NotNull(compiler.Entry);
        var built = Encoding.UTF8.GetString(compiler.EntryBytes);
        Assert.Contains("source new.th", built);
    }

    [Fact]
    public async Task Single_target_build_builds_the_thconfig_text_it_saved()
    {
        var cfg = Path.Combine(_root, "main.thconfig");
        File.WriteAllText(cfg, "source cave.th\nexport map -o old.pdf\nexport model -o cave.lox\n");
        var doc = new FileDocumentViewModel(cfg, File.ReadAllText(cfg), new MeasurementsViewModel());
        doc.DocumentText = "source cave.th\nexport map -o new.pdf\nexport model -o cave.lox\n";
        var (vm, compiler) = MakeBuild(cfg, [doc]);
        var finished = new TaskCompletionSource<BuildOutcome>();
        vm.BuildFinished += (_, o) => finished.TrySetResult(o);
        var mapTarget = Assert.Single(vm.ExportTargets, t => t.Title.Contains("map"));

        mapTarget.BuildCommand.Execute(null);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains("-o new.pdf", File.ReadAllText(cfg));             // saved
        var built = Encoding.UTF8.GetString(compiler.EntryBytes);
        Assert.Contains("export map -o new.pdf", built);                   // and built
    }

    // ---- declared legacy encoding ---------------------------------------------------------

    [Fact]
    public async Task Quick_export_keeps_the_thconfig_in_its_declared_encoding()
    {
        var cfg = Path.Combine(_root, "main.thconfig");
        var latin1 = Encoding.Latin1;
        File.WriteAllBytes(cfg, latin1.GetBytes("encoding iso-8859-1\nsource cave.th\nexport map -o plan.pdf\n# Café\n"));
        var (vm, compiler) = MakeBuild(cfg, []);

        await vm.RunQuickExportAsync("export model -fmt loch -o quick.lox", "quick.lox");

        Assert.NotNull(compiler.Entry);
        // Therion decodes the derived file under the `encoding` line it still carries.
        var asTherionReadsIt = DecodeAsTherionWould(compiler.EntryBytes);
        Assert.Contains("Café", asTherionReadsIt);
    }

    [Fact]
    public async Task Partial_batch_keeps_the_thconfig_in_its_declared_encoding()
    {
        var cfg = Path.Combine(_root, "main.thconfig");
        File.WriteAllBytes(cfg, Encoding.Latin1.GetBytes("encoding iso-8859-1\nsource cave.th\nexport map -o plan.pdf\nexport model -o cave.lox\n# Café\n"));
        var built = new List<byte[]>();
        var env = new BatchBuildEnvironment([cfg], _root,
            (entry, temp, label) =>
            {
                built.Add(File.ReadAllBytes(entry));
                if (temp is not null) File.Delete(temp);
                return Task.FromResult<BuildOutcome?>(new BuildOutcome(entry, true, false, 0, 0, 0, [], null, TimeSpan.Zero, label));
            },
            () => { }, null, null);
        var vm = new BatchBuildViewModel(env);
        vm.Configs[0].Targets[1].IsChecked = false;                        // map only → trimmed copy

        await vm.RunAsync();

        var bytes = Assert.Single(built);
        Assert.Contains("Café", DecodeAsTherionWould(bytes));
    }

    // ---- failure to write the trimmed copy ------------------------------------------------

    [Fact]
    public async Task Partial_batch_with_missing_thconfig_reports_instead_of_throwing()
    {
        var cfg = Path.Combine(_root, "main.thconfig");
        File.WriteAllText(cfg, "source cave.th\nexport map -o plan.pdf\nexport model -o cave.lox\n");
        var env = new BatchBuildEnvironment([cfg], _root,
            (entry, temp, label) => Task.FromResult<BuildOutcome?>(new BuildOutcome(entry, true, false, 0, 0, 0, [], null, TimeSpan.Zero, label)),
            () => { }, null, null);
        var vm = new BatchBuildViewModel(env);
        vm.Configs[0].Targets[1].IsChecked = false;                        // partial → needs the trimmed copy
        File.Delete(cfg);                                                  // gone before Build was pressed

        var thrown = await Record.ExceptionAsync(() => vm.RunAsync());

        Assert.Null(thrown);
        Assert.False(vm.IsRunning);
    }
}
