// A thconfig's `system` lines are shell commands Therion runs while it compiles. Whatever route an agent
// takes to a build — run_build with or without an entry point, run_command Build or Rebuild, the same
// tools reached through the in-process dispatcher that `call` and macros use — the host's approval
// decides whether those commands may run. This file pins that as a matrix (route × where the system
// line sits × what the host answers) rather than one example per route, and guards the tool catalogue
// so a new tool or command that can start the compiler has to be classified here first.

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Therion.Mcp.Dispatch;
using Therion.Mcp.Tools;
using Therion.Processing.Abstractions;

namespace Therion.Mcp.Tests;

public class AgentBuildTrustMatrixTests
{
    // ---- the guard: every tool and every runnable command is classified ------------------------------

    private enum Reach
    {
        /// <summary>Starts the Therion compiler, so it must ask the build approval first.</summary>
        Compiles,
        /// <summary>Saves through the IDE: the app's save hooks run, and they ask the folder's trust.</summary>
        SavesInIde,
        /// <summary>Runs nothing the project defines.</summary>
        Inert,
    }

    private static readonly Dictionary<string, Reach> ToolReach = new(StringComparer.Ordinal)
    {
        ["run_build"] = Reach.Compiles,
        ["run_command"] = Reach.Compiles,
        ["save_all"] = Reach.SavesInIde,
    };

    private static readonly string[] InertTools =
    [
        "add_map_members", "convert_coordinates", "convert_units", "data_quality_report", "deps_graph",
        "describe_command", "drawing_status", "edit_file", "explain_diagnostic", "export_gis", "export_tables",
        "find_equate_candidates", "find_references", "focus_tool", "format_file", "generate_report",
        "get_declination", "get_diagnostics", "get_file_outline", "get_open_documents", "get_setting",
        "get_ui_state", "goto_definition", "goto_symbol", "import_survey", "list_commands", "list_files",
        "list_leads", "list_settings", "list_stations", "list_survey_info", "list_symbols", "list_todos",
        "load_workspace", "open_file", "project_metadata_get", "project_metadata_set", "query_legs",
        "read_file", "rename_symbol", "reset_layout", "scaffold_th2", "scaffold_topodroid_project",
        "search_thbook", "server_info", "set_active_thconfig", "set_layout", "set_lead_status", "set_setting",
        "show_in_3d", "show_toast", "structural_analysis", "survey_graph", "survey_stats", "workspace_info",
    ];

    private static IEnumerable<(string Name, Type Type)> AllTools() =>
        typeof(BuildTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => (Method: m, Attr: m.GetCustomAttribute<McpServerToolAttribute>()))
                .Where(x => x.Attr is not null)
                .Select(x => (x.Attr!.Name ?? x.Method.Name, t)));

    [Fact]
    public void Every_agent_tool_is_classified_by_whether_it_can_run_project_commands()
    {
        var tools = AllTools().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var classified = ToolReach.Keys.Concat(InertTools).ToList();

        Assert.Equal(classified.Count, classified.Distinct(StringComparer.Ordinal).Count());
        var unclassified = tools.Except(classified, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(unclassified.Count == 0,
            "New agent tools must be classified here (can they start the compiler or run project commands?): "
            + string.Join(", ", unclassified));
        var stale = classified.Except(tools, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "Classified tools that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_tool_type_that_can_reach_the_compiler_takes_the_build_approval()
    {
        // A tool that is handed the compiler, or that builds through the IDE, must also be handed the
        // approval: without it there is no one to ask, and no way to refuse.
        var compiling = AllTools().Where(t => ToolReach.GetValueOrDefault(t.Name, Reach.Inert) == Reach.Compiles)
            .Select(t => t.Type).ToHashSet();
        var withCompiler = typeof(BuildTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .Where(t => t.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ITherionCompiler))));

        foreach (var type in compiling.Concat(withCompiler).Distinct())
            Assert.True(type.GetConstructors().Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(IBuildApproval))),
                $"{type.Name} can start a build but its constructor takes no {nameof(IBuildApproval)}");
    }

    private static readonly Dictionary<string, Reach> CommandReach = new(StringComparer.Ordinal)
    {
        [ShellCommandIds.Build] = Reach.Compiles,
        [ShellCommandIds.Rebuild] = Reach.Compiles,
        [ShellCommandIds.Save] = Reach.SavesInIde,
    };

    /// <summary>
    /// Runnable commands that start nothing the project defines. Opening Loch, Aven or the output folder
    /// starts a fixed viewer on the build's outputs, not a command from the project.
    /// </summary>
    private static readonly string[] InertCommands =
    [
        ShellCommandIds.CancelBuild, ShellCommandIds.OpenInLoch, ShellCommandIds.OpenInAven, ShellCommandIds.OpenOutputFolder,
        ShellCommandIds.ToggleWorkspaceExplorer, ShellCommandIds.ToggleDiagnostics, ShellCommandIds.ToggleObjectBrowser,
        ShellCommandIds.ToggleOutline, ShellCommandIds.ToggleProject, ShellCommandIds.ToggleLog, ShellCommandIds.ToggleLivePreview,
        ShellCommandIds.ToggleMapViewer, ShellCommandIds.ToggleModel3DViewer, ShellCommandIds.ToggleStructuralGeology,
        ShellCommandIds.ToggleBlenderAnimation, ShellCommandIds.SplitEditor, ShellCommandIds.ResetLayout,
        ShellCommandIds.FloatActiveDocument, ShellCommandIds.ToggleWordWrap, ShellCommandIds.ToggleFullScreen,
        ShellCommandIds.GoBack, ShellCommandIds.GoForward, ShellCommandIds.NextProblem, ShellCommandIds.PreviousProblem,
        ShellCommandIds.QuickOpen, ShellCommandIds.FindInFiles, ShellCommandIds.ReplaceInFiles, ShellCommandIds.ReopenClosedTab,
        ShellCommandIds.RenameSymbol,
    ];

    [Fact]
    public void Every_command_an_agent_can_run_is_classified_by_whether_it_builds()
    {
        var runnable = R3CommandPolicy.RunnableCommands.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var classified = CommandReach.Keys.Concat(InertCommands).ToHashSet(StringComparer.Ordinal);

        var unclassified = runnable.Except(classified).ToList();
        Assert.True(unclassified.Count == 0,
            "Commands run_command can run must be classified here (do they build?): " + string.Join(", ", unclassified));
        Assert.Empty(classified.Except(runnable));
    }

    [Fact]
    public async Task Every_runnable_command_that_builds_is_held_back_when_the_host_declines()
    {
        using var ws = Layout.Create(LayoutKind.MainThconfig);
        foreach (var command in R3CommandPolicy.RunnableCommands.Where(c => CommandReach.TryGetValue(c.Id, out var reach) && reach == Reach.Compiles))
        {
            var bridge = new Bridge();
            var approval = new Approval(answer: false);
            await using var host = new WorkspaceHost(ws.Entry);
            var result = await new CommandTools(bridge, host, approval).RunCommand(command.Id, confirm: true);

            Assert.Equal(ToolErrorCodes.NotTrusted, result.Error?.Code);
            Assert.Null(bridge.Ran);
            Assert.Equal(1, approval.Asked);
        }
    }

    // ---- the matrix ----------------------------------------------------------------------------------

    public enum LayoutKind
    {
        /// <summary>The thconfig runs nothing.</summary>
        NoSystemLines,
        /// <summary>A system line in the thconfig itself.</summary>
        MainThconfig,
        /// <summary>One in a fragment the thconfig pulls in with <c>input</c>, behind a trailing comment.</summary>
        Fragment,
        /// <summary>One two fragments deep, the first input quoted, in a sub-folder.</summary>
        NestedFragment,
        /// <summary>Two system lines, one indented and spelled in capitals, one in a fragment.</summary>
        SeveralSpellings,
        /// <summary>The entry point is a .th file: Therion compiles it with its defaults, which run nothing.</summary>
        SurveyEntry,
        /// <summary>A fragment outside the opened folder, reached with <c>../</c>.</summary>
        FragmentOutsideTheFolder,
        /// <summary>A fragment reached through a link inside the folder to a file outside it.</summary>
        FragmentThroughALink,
    }

    public enum Route { RunBuildDefault, RunBuildNamedEntry, RunCommandBuild, RunCommandRebuildOfTheIdesTarget, DispatchedRunBuild, DispatchedRunCommand }

    public enum Answer { NoneRegistered, HeadlessRefusal, Declines, Approves, AllowAll }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var layout in Enum.GetValues<LayoutKind>())
            foreach (var route in Enum.GetValues<Route>())
                yield return [layout, route];
    }

    /// <summary>The layouts whose system lines the gate does not see today.</summary>
    private static bool KnownGap(LayoutKind layout) =>
        layout is LayoutKind.FragmentOutsideTheFolder or LayoutKind.FragmentThroughALink;

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task An_agent_build_runs_system_commands_only_when_the_host_approves(LayoutKind layout, Route route)
    {
        if (KnownGap(layout)) return;   // see the skipped theory below
        await CheckEveryAnswer(layout, route);
    }

    public static IEnumerable<object[]> GapMatrix()
    {
        foreach (var layout in Enum.GetValues<LayoutKind>().Where(KnownGap))
            foreach (var route in Enum.GetValues<Route>())
                yield return [layout, route];
    }

    [Theory]
    [MemberData(nameof(GapMatrix))]
    public async Task An_agent_build_asks_about_system_commands_in_fragments_outside_the_folder(LayoutKind layout, Route route)
    {
        await CheckEveryAnswer(layout, route);
    }

    private static async Task CheckEveryAnswer(LayoutKind layout, Route route)
    {
        if (OperatingSystem.IsWindows() && layout == LayoutKind.FragmentThroughALink) return;   // symlinks need privileges there
        using var ws = Layout.Create(layout);
        foreach (var answer in Enum.GetValues<Answer>())
        {
            var label = $"{layout} × {route} × {answer}";
            var compiler = new Compiler();
            var bridge = new Bridge();
            var approval = answer switch
            {
                Answer.Declines => new Approval(answer: false),
                Answer.Approves => new Approval(answer: true),
                _ => null,
            };
            IBuildApproval? registered = answer switch
            {
                Answer.HeadlessRefusal => RefuseSystemCommandsBuildApproval.Instance,
                Answer.AllowAll => AllowAllBuildApproval.Instance,
                _ => approval,
            };

            var (ok, code) = await Run(ws, route, registered, compiler, bridge);

            bool runsCommands = ws.Planted.Count > 0;
            bool approves = answer is Answer.Approves or Answer.AllowAll;
            int launches = compiler.Compiled.Count + (bridge.Ran is null ? 0 : 1);
            if (runsCommands && !approves)
            {
                Assert.True(code == ToolErrorCodes.NotTrusted, $"{label}: expected not_trusted, got ok={ok} code={code}");
                Assert.True(launches == 0, $"{label}: the build started although the host did not approve its system commands");
            }
            else
            {
                Assert.True(ok, $"{label}: expected the build to start, got {code}");
                Assert.True(launches == 1, $"{label}: expected one build, saw {launches}");
            }

            if (approval is not null)
            {
                Assert.True(approval.Asked == (runsCommands ? 1 : 0),
                    $"{label}: the host was asked {approval.Asked} time(s); expected {(runsCommands ? 1 : 0)}");
                if (runsCommands)
                {
                    foreach (var planted in ws.Planted)
                        Assert.True(approval.Commands.Any(c => c.Contains(planted, StringComparison.Ordinal)),
                            $"{label}: the host was not shown '{planted}' (it saw: {string.Join(" | ", approval.Commands)})");
                    Assert.Equal(ws.Root, approval.Root);
                }
            }
        }
    }

    private static async Task<(bool Ok, string? Code)> Run(
        Layout ws, Route route, IBuildApproval? approval, Compiler compiler, Bridge bridge)
    {
        switch (route)
        {
            case Route.RunBuildDefault:
            {
                await using var host = new WorkspaceHost(ws.Entry);
                var r = await new BuildTools(host, compiler, approval).RunBuild();
                return (r.Ok, r.Error?.Code);
            }
            case Route.RunBuildNamedEntry:
            {
                await using var host = new WorkspaceHost(ws.Entry);
                var r = await new BuildTools(host, compiler, approval).RunBuild(ws.EntryRelative);
                return (r.Ok, r.Error?.Code);
            }
            case Route.RunCommandBuild:
            {
                await using var host = new WorkspaceHost(ws.Entry);
                var r = await new CommandTools(bridge, host, approval).RunCommand(ShellCommandIds.Build);
                return (r.Ok, r.Error?.Code);
            }
            case Route.RunCommandRebuildOfTheIdesTarget:
            {
                // The IDE builds its own target; the workspace was opened on a plain survey.
                bridge.BuildEntry = ws.Entry;
                await using var host = new WorkspaceHost(ws.Survey);
                var r = await new CommandTools(bridge, host, approval).RunCommand(ShellCommandIds.Rebuild);
                return (r.Ok, r.Error?.Code);
            }
            default:
            {
                // An instance registration is not disposed by the provider, so the host is disposed here.
                await using var host = new WorkspaceHost(ws.Entry);
                var services = new ServiceCollection();
                services.AddSingleton<IUiBridge>(bridge);
                services.AddSingleton<IWorkspaceHost>(host);
                services.AddSingleton<ITherionCompiler>(compiler);
                if (approval is not null) services.AddSingleton(approval);
                services.AddToolServices();
                await using var sp = services.BuildServiceProvider();
                var dispatcher = new ToolDispatcher(sp, McpProfile.Full, includeUi: true);
                var outcome = route == Route.DispatchedRunBuild
                    ? await dispatcher.CallAsync("run_build", JsonSerializer.SerializeToElement(new { }))
                    : await dispatcher.CallAsync("run_command", JsonSerializer.SerializeToElement(new { commandId = ShellCommandIds.Build }));
                Assert.True(outcome.Found);
                string? code = outcome.Result.ValueKind == JsonValueKind.Object
                    && outcome.Result.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                    && e.TryGetProperty("code", out var c) ? c.GetString() : null;
                return (outcome.Ok, code ?? (outcome.Ok ? null : outcome.Error));
            }
        }
    }

    // ---- fixtures ------------------------------------------------------------------------------------

    /// <summary>An invented project laid out for one <see cref="LayoutKind"/>.</summary>
    private sealed class Layout : IDisposable
    {
        private readonly string _scratch;
        public string Root { get; }
        public string Entry { get; }
        public string EntryRelative { get; }
        public string Survey { get; }
        public List<string> Planted { get; } = [];

        private Layout(string scratch, string root, string entry, string survey)
        {
            _scratch = scratch;
            Root = root;
            Entry = entry;
            // The host opens the entry's own folder, so a named entry is relative to that.
            EntryRelative = Path.GetFileName(entry);
            Survey = survey;
        }

        public void Dispose()
        {
            try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
        }

        public static Layout Create(LayoutKind kind)
        {
            var scratch = WorkspacePaths.Canonicalize(Directory.CreateTempSubdirectory("thide-agent-trust-").FullName);
            var root = Path.Combine(scratch, "project");
            Directory.CreateDirectory(Path.Combine(root, "caves"));
            var survey = Path.Combine(root, "caves", "cave.th");
            File.WriteAllText(survey, "survey cave\n  centreline\n    data normal from to length compass clino\n    1 2 10.0 90 0\n  endcentreline\nendsurvey\n");
            var cfg = Path.Combine(root, "project.thconfig");
            var layout = new Layout(scratch, root, kind == LayoutKind.SurveyEntry ? survey : cfg, survey);

            string Plant(string name)
            {
                layout.Planted.Add(name);
                return $"system \"echo {name}\"\n";
            }

            var main = "source caves/cave.th\n";
            switch (kind)
            {
                case LayoutKind.NoSystemLines:
                case LayoutKind.SurveyEntry:
                    main += "export model -o cave.lox\n";
                    break;
                case LayoutKind.MainThconfig:
                    main += Plant("planted-main");
                    break;
                case LayoutKind.Fragment:
                    main += "input extra.thc # shared settings\n";
                    File.WriteAllText(Path.Combine(root, "extra.thc"), Plant("planted-fragment"));
                    break;
                case LayoutKind.NestedFragment:
                    Directory.CreateDirectory(Path.Combine(root, "config"));
                    main += "input \"config/first.thc\"\n";
                    File.WriteAllText(Path.Combine(root, "config", "first.thc"), "input second.thc\n");
                    File.WriteAllText(Path.Combine(root, "config", "second.thc"), Plant("planted-nested"));
                    break;
                case LayoutKind.SeveralSpellings:
                    main += "  " + Plant("planted-indented").Replace("system", "SYSTEM") + "input extra.thc\n";
                    File.WriteAllText(Path.Combine(root, "extra.thc"), Plant("planted-second"));
                    break;
                case LayoutKind.FragmentOutsideTheFolder:
                    Directory.CreateDirectory(Path.Combine(scratch, "shared"));
                    main += "input ../shared/common.thc\n";
                    File.WriteAllText(Path.Combine(scratch, "shared", "common.thc"), Plant("planted-outside"));
                    break;
                case LayoutKind.FragmentThroughALink:
                    Directory.CreateDirectory(Path.Combine(scratch, "shared"));
                    File.WriteAllText(Path.Combine(scratch, "shared", "common.thc"), Plant("planted-linked"));
                    File.CreateSymbolicLink(Path.Combine(root, "common.thc"), Path.Combine(scratch, "shared", "common.thc"));
                    main += "input common.thc\n";
                    break;
            }
            File.WriteAllText(cfg, main);
            return layout;
        }
    }

    private sealed class Approval(bool answer) : IBuildApproval
    {
        public int Asked { get; private set; }
        public string? Root { get; private set; }
        public List<string> Commands { get; } = [];

        public Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
            CancellationToken ct = default)
        {
            Asked++;
            Root = root;
            Commands.AddRange(systemCommands);
            return Task.FromResult(answer);
        }
    }

    private sealed class Compiler : ITherionCompiler
    {
        public List<string> Compiled { get; } = [];

        public ValueTask<CompileResult> CompileAsync(
            string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            lock (Compiled) Compiled.Add(entryPointPath);
            return ValueTask.FromResult(new CompileResult(0, [], []));
        }
    }

    private sealed class Bridge : IUiBridge
    {
        public string? Ran { get; private set; }
        public string? BuildEntry { get; set; }

        public bool IsAvailable => true;
        public bool FollowAgent => true;

        public Task<UiActionResult> RunCommandAsync(string commandId)
        {
            Ran = commandId;
            return Task.FromResult(new UiActionResult(true, $"Ran '{commandId}'."));
        }

        public Task<string?> GetBuildEntryAsync() => Task.FromResult(BuildEntry);

        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
        public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);
        public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
            Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);
        public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => Ok();
        public Task<UiActionResult> FocusToolAsync(string toolId) => Ok();
        public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => Ok();
        public Task<UiActionResult> ShowInThreeDAsync(string station) => Ok();
        public Task<UiActionResult> ShowToastAsync(string message, string kind) => Ok();
        private static Task<UiActionResult> Ok() => Task.FromResult(new UiActionResult(true, "ok"));
    }
}
