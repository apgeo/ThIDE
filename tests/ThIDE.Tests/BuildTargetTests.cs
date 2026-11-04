// What Compile builds and how the UI names it: always the active thconfig, whichever tab has focus;
// a focused thconfig is built only through "Build this thconfig". Generated Files lists the outputs of
// one thconfig, keeps a failed build's stale flag across a restart, and the orphan banner and startup
// arguments follow the active thconfig too.
// The view models marshal session/document events to the UI thread; the steps that raise them run
// under UiThread.Run so that marshalling executes inline.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Therion.Build;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]   // one test sets the UI language
public class BuildTargetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-target-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    /// <summary>Records what it built and the status shown while it ran; returns the configured outputs.</summary>
    private sealed class RecordingCompiler : ITherionCompiler
    {
        public readonly List<string> Built = [];
        public string StatusDuringBuild = string.Empty;
        public BuildViewModel? Vm;
        public int ExitCode;
        public Func<string, ImmutableArray<OutputArtifact>> Outputs = _ => ImmutableArray<OutputArtifact>.Empty;
        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            Built.Add(entryPointPath);
            StatusDuringBuild = Vm?.Status ?? string.Empty;
            return new(new CompileResult(ExitCode, ImmutableArray<Therion.Core.Diagnostic>.Empty, Outputs(entryPointPath)));
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

    /// <summary>A session whose active thconfig and change event the test drives.</summary>
    private sealed class Session
    {
        public string? Active;
        public string? Root;
        public EventHandler? Changed;
        public readonly IWorkspaceSession Proxy;
        public Session()
        {
            Proxy = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name switch
            {
                "get_ActiveThconfig" => Active is null ? null : new ThconfigCandidate(Active, Path.GetFileName(Active), false),
                "get_RootPath" => Root,
                "get_Candidates" => new List<ThconfigCandidate>(),
                "Covers" => true,
                _ => null,
            }, (m, args) => { if (m.Name == "add_Changed") Changed += (EventHandler)args[0]!; });
        }
        public void SetActive(string path) { Active = path; Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>Documents with a focused path the test sets.</summary>
    private sealed class Documents
    {
        public string? Current;
        public EventHandler? DocumentChanged;
        public readonly List<(string Name, object?[] Args)> Calls = [];
        public bool ActivateResult = true;
        public readonly IDocumentService Proxy;
        public Documents()
        {
            var docs = new ObservableCollection<FileDocumentViewModel>();
            Proxy = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
            {
                "get_Documents" => docs,
                "get_CurrentPath" => Current,
                "get_CurrentText" => string.Empty,
                "get_CurrentDiagnostics" => ImmutableArray<Therion.Core.Diagnostic>.Empty,
                "ActivateThconfigAsync" => Task.FromResult(ActivateResult),
                "OpenFileAsync" => Task.CompletedTask,
                "SaveDocumentAsync" => Task.FromResult(true),
                _ => DefaultFor(m),
            }, (m, args) =>
            {
                if (m.Name == "add_DocumentChanged") DocumentChanged += (EventHandler)args[0]!;
                Calls.Add((m.Name, args));
            });
        }
        public void Focus(string? path) { Current = path; DocumentChanged?.Invoke(this, EventArgs.Empty); }

        // Task-returning members the test does not name still have to return a task.
        private static object? DefaultFor(MethodInfo m) =>
            m.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }

    private string Thconfig(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "source cave.th\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
        return path;
    }

    /// <summary>An output file written after the inputs, as the build's artifact.</summary>
    private OutputArtifact Output(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "x");
        var info = new FileInfo(path);
        return new OutputArtifact(path, Path.GetExtension(name).TrimStart('.'), info.Length, info.LastWriteTimeUtc);
    }

    private (BuildViewModel Vm, RecordingCompiler Compiler) MakeBuild(Session session, Documents docs, IOutputArtifactCache? cache = null)
    {
        var compiler = new RecordingCompiler();
        var vm = new BuildViewModel(compiler, new CompileGate(), new NoShell(), new NoTools(), docs.Proxy,
            cache ?? new JsonOutputArtifactCache(Path.Combine(_root, "cache")), session.Proxy,
            new FakeSettings(AppSettings.Default with { AutoSaveBeforeCompile = true }));
        compiler.Vm = vm;
        return (vm, compiler);
    }

    // ---- Compile builds the active thconfig, "Build this thconfig" the focused one ----------------

    [Fact]
    public async Task Compile_builds_the_active_thconfig_even_when_another_thconfig_has_focus()
    {
        var a = Thconfig("a.thconfig");
        var b = Thconfig("b.thconfig");
        var session = new Session { Active = b, Root = _root };
        var docs = new Documents { Current = a };           // a's tab has focus, b is active
        var (vm, compiler) = MakeBuild(session, docs);

        Assert.Equal(b, vm.ResolveBuildEntry());
        Assert.Equal("b.thconfig", vm.BuildTargetName);     // what the Compile tooltip names
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal([b], compiler.Built);
    }

    [Fact]
    public async Task Build_this_thconfig_builds_the_focused_thconfig_without_changing_the_target()
    {
        var a = Thconfig("a.thconfig");
        var b = Thconfig("b.thconfig");
        var session = new Session { Active = b, Root = _root };
        var docs = new Documents { Current = a };
        var (vm, compiler) = MakeBuild(session, docs);

        Assert.Equal(a, vm.FocusedOtherThconfig);
        Assert.True(vm.BuildThisThconfigCommand.CanExecute(null));
        await vm.BuildThisThconfigCommand.ExecuteAsync(null);

        Assert.Equal([a], compiler.Built);
        Assert.Equal(b, vm.ResolveBuildEntry());
    }

    [Fact]
    public void Build_this_thconfig_is_unavailable_when_the_focused_tab_is_the_active_thconfig_or_not_a_thconfig()
    {
        var b = Thconfig("b.thconfig");
        var th = Path.Combine(_root, "cave.th");
        File.WriteAllText(th, "survey cave\nendsurvey\n");
        var session = new Session { Active = b, Root = _root };
        var docs = new Documents { Current = b };
        var (vm, _) = MakeBuild(session, docs);

        Assert.False(vm.BuildThisThconfigCommand.CanExecute(null));
        UiThread.Run(() => docs.Focus(th));
        Assert.False(vm.BuildThisThconfigCommand.CanExecute(null));
    }

    [Fact]
    public void Selecting_another_thconfig_makes_it_the_named_target() => UiThread.Run(() =>
    {
        var a = Thconfig("a.thconfig");
        var b = Thconfig("b.thconfig");
        var session = new Session { Active = a, Root = _root };
        var docs = new Documents { Current = a };
        var (vm, _) = MakeBuild(session, docs);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        session.SetActive(b);

        Assert.Equal("b.thconfig", vm.BuildTargetName);
        Assert.Contains(nameof(BuildViewModel.BuildTargetName), changed);   // the tooltip re-reads it
    });

    [Fact]
    public async Task Status_and_first_output_line_name_the_target()
    {
        var b = Thconfig("b.thconfig");
        var session = new Session { Active = b, Root = _root };
        var docs = new Documents { Current = null };
        var (vm, compiler) = MakeBuild(session, docs);

        await vm.BuildCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(Tr.Get("Build_CompilingTarget"), "b.thconfig"), compiler.StatusDuringBuild);
        var first = vm.Output[0];
        Assert.Equal(string.Format(Tr.Get("Build_OutputTarget"), b), first.Text);
        Assert.Equal(b, first.LinkPath);                    // a link that opens the thconfig
        Assert.Equal("b.thconfig", vm.LastBuildTarget);
    }

    [Fact]
    public async Task Dashboard_last_build_line_names_what_was_built()
    {
        var a = Thconfig("a.thconfig");
        var b = Thconfig("b.thconfig");
        var session = new Session { Active = b, Root = _root };
        var docs = new Documents { Current = a };
        var (vm, _) = MakeBuild(session, docs);
        var dashboard = new ProjectDashboardViewModel(session.Proxy, docs.Proxy, vm);

        await vm.BuildThisThconfigCommand.ExecuteAsync(null);   // builds a, while b is the entry config
        dashboard.RefreshCommand.Execute(null);

        Assert.Equal(string.Format(Tr.Get("Dash_BuildOk"), "a.thconfig"), dashboard.BuildStatus);
    }

    // ---- Generated Files is keyed by thconfig ---------------------------------------------------

    [Fact]
    public async Task Generated_files_follow_the_active_thconfig_and_say_when_it_was_never_built()
    {
        var a = Thconfig("a.thconfig");
        var b = Thconfig("b.thconfig");
        var session = new Session { Active = a, Root = _root };
        var docs = new Documents { Current = a };
        var (vm, compiler) = MakeBuild(session, docs);
        var lox = Output("a.lox");
        compiler.Outputs = _ => [lox];

        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Equal(lox.Path, Assert.Single(vm.Artifacts).Path);
        Assert.StartsWith(string.Format(Tr.Get("Gen_HeaderBuilt"), "a.thconfig", ""), vm.ArtifactsHeader);

        UiThread.Run(() =>
        {
            session.SetActive(b);                           // b has never been built

            Assert.Empty(vm.Artifacts);
            Assert.Equal(string.Format(Tr.Get("Gen_Header"), "b.thconfig"), vm.ArtifactsHeader);
            Assert.Equal(string.Format(Tr.Get("Gen_NotBuiltYet"), "b.thconfig"), vm.ArtifactsEmptyText);
            Assert.True(vm.CanCompileFromEmptyState);

            session.SetActive(a);                           // back: a's outputs, from the cache

            Assert.Equal(lox.Path, Assert.Single(vm.Artifacts).Path);
            Assert.False(vm.HasArtifactsEmptyText);
        });
    }

    [Fact]
    public async Task Focusing_another_thconfig_tab_does_not_swap_in_its_outputs()
    {
        var a = Thconfig("a.thconfig");
        var b = Thconfig("b.thconfig");
        var cache = new JsonOutputArtifactCache(Path.Combine(_root, "cache"));
        var stray = Output("b.lox");
        cache.Save(b, "unknown", [stray]);                  // b was built some time ago
        var session = new Session { Active = a, Root = _root };
        var docs = new Documents { Current = a };
        var (vm, compiler) = MakeBuild(session, docs, cache);
        var lox = Output("a.lox");
        compiler.Outputs = _ => [lox];
        await vm.BuildCommand.ExecuteAsync(null);

        UiThread.Run(() =>
        {
            docs.Focus(b);
            Assert.Equal(lox.Path, Assert.Single(vm.Artifacts).Path);
        });
    }

    [Fact]
    public async Task A_failed_build_keeps_earlier_outputs_marked_stale_across_a_restart()
    {
        var a = Thconfig("a.thconfig");
        var cacheDir = Path.Combine(_root, "cache");
        var session = new Session { Active = a, Root = _root };
        var docs = new Documents { Current = a };
        var (vm, compiler) = MakeBuild(session, docs, new JsonOutputArtifactCache(cacheDir));
        var lox = Output("a.lox");
        compiler.Outputs = _ => [lox];
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.False(Assert.Single(vm.Artifacts).IsStale);

        compiler.ExitCode = 1;                              // the next build fails and writes nothing
        compiler.Outputs = _ => ImmutableArray<OutputArtifact>.Empty;
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(vm.Artifacts).IsStale);
        Assert.True(vm.HasStaleArtifacts);
        Assert.Equal(string.Format(Tr.Get("Gen_StaleAfterFailedBuild"), "a.thconfig"), vm.StaleBannerText);

        // Restart: a new view model over the same cache still shows them as stale.
        var (restarted, _) = MakeBuild(session, docs, new JsonOutputArtifactCache(cacheDir));
        Assert.True(Assert.Single(restarted.Artifacts).IsStale);
        Assert.True(restarted.HasStaleArtifacts);
    }

    // ---- the orphan banner ----------------------------------------------------------------------

    [Fact]
    public void Orphan_banner_says_not_part_of_the_active_thconfig_and_can_be_dismissed() => UiThread.Run(() =>
    {
        var doc = new FileDocumentViewModel(Path.Combine(_root, "cave.th"), "", new MeasurementsViewModel());

        doc.SetWorkspaceMembership(false, "main.thconfig");
        Assert.True(doc.IsOrphan);
        Assert.Equal(string.Format(Tr.Get("File_NotInActiveThconfig"), "main.thconfig"), doc.OrphanBannerText);
        Assert.DoesNotContain("relational tree", doc.OrphanBannerText);

        doc.DismissOrphanBannerCommand.Execute(null);
        Assert.False(doc.IsOrphan);
        doc.SetWorkspaceMembership(false, "main.thconfig");    // the same project rebuilt: stays dismissed
        Assert.False(doc.IsOrphan);
        doc.SetWorkspaceMembership(false, "other.thconfig");   // another thconfig: shown again
        Assert.True(doc.IsOrphan);
    });

    [Fact]
    public void Orphan_banner_on_a_thconfig_names_the_active_one_in_the_ui_language() => UiThread.Run(() =>
    {
        var doc = new FileDocumentViewModel(Path.Combine(_root, "old.thconfig"), "", new MeasurementsViewModel());
        UiLanguageCollection.In(new CultureInfo("ro-RO"), () =>
        {
            doc.SetWorkspaceMembership(false, "new.thconfig");
            Assert.Equal("Nu este thconfig-ul activ (cel activ este new.thconfig).", doc.OrphanBannerText);
        });
    });

    // ---- a thconfig passed on the command line becomes the active configuration ------------------

    [Fact]
    public async Task Startup_thconfig_argument_is_activated_and_other_files_open_as_tabs()
    {
        var cfg = Thconfig("main.thconfig");
        var th = Path.Combine(_root, "cave.th");
        File.WriteAllText(th, "survey cave\nendsurvey\n");
        var docs = new Documents();
        MainWindowViewModel shell = null!;
        UiThread.Run(() => shell = MainWindowViewModelHost.Create(docs.Proxy));
        docs.Calls.Clear();

        await shell.OpenStartupFilesAsync([th, cfg]);

        var activate = Assert.Single(docs.Calls, c => c.Name == "ActivateThconfigAsync");
        Assert.Equal(cfg, activate.Args[0]);
        Assert.True(((ThconfigActivation)activate.Args[1]!).OpenInEditor);
        var opened = docs.Calls.Where(c => c.Name == "OpenFileAsync").Select(c => (string)c.Args[0]!).ToList();
        Assert.Equal([th], opened);
    }

    [Fact]
    public async Task Startup_thconfig_that_fails_to_load_still_opens_as_a_tab()
    {
        var cfg = Thconfig("main.thconfig");
        var docs = new Documents { ActivateResult = false };
        MainWindowViewModel shell = null!;
        UiThread.Run(() => shell = MainWindowViewModelHost.Create(docs.Proxy));
        docs.Calls.Clear();

        await shell.OpenStartupFilesAsync([cfg]);

        Assert.Equal([cfg], docs.Calls.Where(c => c.Name == "OpenFileAsync").Select(c => (string)c.Args[0]!));
    }
}
