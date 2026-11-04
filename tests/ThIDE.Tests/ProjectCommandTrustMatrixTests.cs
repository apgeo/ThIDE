// Nothing a project defines runs on this machine until the user trusts the folder. That promise has
// many entry points — the project's hooks (attached and detached), its tasks (started by the user or by
// a hook; shell, built-in build and macro tasks) and the agent's builds of a thconfig with `system`
// lines — and each asks the same trust question in its own way. This matrix crosses every entry point
// with every trust state and checks the three things that matter: whether anything ran, how many times
// the user was asked, and what was remembered. Real hooks and tasks are started as shell commands that
// leave a marker file, so "ran" means a process really started.

using System.Collections.ObjectModel;
using Therion.Build.Automation;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class ProjectCommandTrustMatrixTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-trust-matrix-").FullName;

    /// <summary>
    /// A detached hook is not waited for, and its marker appears before its shell has exited. On
    /// Windows a folder that is a running process's working directory cannot be deleted, so the
    /// cleanup waits for that shell to go.
    /// </summary>
    public void Dispose()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline) { Thread.Sleep(100); }
        }
    }

    public enum Entry
    {
        ProjectSaveHook,
        ProjectDetachedHook,
        ShellTaskStartedByTheUser,
        ShellTaskStartedByAHook,
        BuildTask,
        MacroTask,
        AgentBuildWithSystemCommands,
    }

    public enum State
    {
        Trusted,
        AcceptsWhenAsked,
        DeclinesWhenAsked,
        /// <summary>The user said no to an automatic run earlier in this session, and says no again if asked.</summary>
        DeclinedEarlierThisSession,
        ProjectAutomationOff,
        NoFolderOpen,
    }

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var entry in Enum.GetValues<Entry>())
            foreach (var state in Enum.GetValues<State>())
            {
                // The agent's build always has a folder, and its trust question does not depend on the
                // project-automation switch (that switch is about the project's own config files).
                if (entry == Entry.AgentBuildWithSystemCommands && state is State.ProjectAutomationOff or State.NoFolderOpen) continue;
                yield return [entry, state];
            }
    }

    /// <summary>Hooks, and tasks a hook starts, run on their own: after one "no" they are skipped quietly.</summary>
    private static bool StartsOnItsOwn(Entry entry) => entry is Entry.ProjectSaveHook or Entry.ProjectDetachedHook or Entry.ShellTaskStartedByAHook;

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_project_command_runs_only_in_a_folder_the_user_trusts(Entry entry, State state)
    {
        var root = Path.Combine(_dir, "cave");
        Directory.CreateDirectory(Path.Combine(root, ".thide"));
        File.WriteAllText(Path.Combine(root, ".thide", "hooks.json"), $$"""
            { "hooks": [ { "event": "FileSaved", "command": "echo ran > hook.marker", "detached": {{(entry == Entry.ProjectDetachedHook ? "true" : "false")}} } ] }
            """);
        File.WriteAllText(Path.Combine(root, ".thide", "tasks.json"), """
            { "tasks": [
                { "id": "shell", "title": "Shell", "command": "echo ran > task.marker" },
                { "id": "build", "title": "Build", "type": "Builtin", "builtin": "build" },
                { "id": "macro", "title": "Macro", "type": "Macro", "macro": "tidy" }
            ] }
            """);

        bool answer = state == State.AcceptsWhenAsked;
        int asked = 0;
        string? askedAbout = null;
        var trust = new WorkspaceTrustService(Path.Combine(_dir, "trust.json"), (folder, _) =>
        {
            asked++;
            askedAbout = folder;
            return Task.FromResult(answer);
        });
        if (state == State.Trusted) trust.Trust(root);
        if (state == State.DeclinedEarlierThisSession)
        {
            Assert.False((await trust.EnsureTrustedOnceAsync(root, "hooks")).Trusted);
            asked = 0;
        }

        var settings = new FakeSettings(AppSettings.Default with { EnableProjectAutomation = state != State.ProjectAutomationOff });
        var sessionRoot = state == State.NoFolderOpen ? null : root;
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "get_RootPath" ? sessionRoot : null);
        var output = new FakeOutput();
        var notifications = new FakeNotifications();
        int builds = 0, macros = 0;
        bool? approved = null;

        switch (entry)
        {
            case Entry.ProjectSaveHook:
            case Entry.ProjectDetachedHook:
                var hooks = new ScriptHookService(settings, session: session, trust: trust, output: output, notifications: notifications);
                await hooks.RunAsync(new HookContext(HookEvent.FileSaved, Path.Combine(root, "a.th"), sessionRoot));
                break;
            case Entry.AgentBuildWithSystemCommands:
                approved = await new TrustBuildApproval(trust).ApproveSystemCommandsAsync(
                    root, Path.Combine(root, "cave.thconfig"), ["echo from-the-project"]);
                break;
            default:
                var tasks = new TaskRunnerService(settings, session, trust, output, notifications: notifications)
                {
                    RunBuild = (_, _) => { builds++; return Task.CompletedTask; },
                    RunMacro = _ => { macros++; return Task.CompletedTask; },
                };
                var (id, context) = entry switch
                {
                    Entry.ShellTaskStartedByTheUser => ("shell", (HookContext?)null),
                    Entry.ShellTaskStartedByAHook => ("shell", new HookContext(HookEvent.FileSaved, Path.Combine(root, "a.th"), root)),
                    Entry.BuildTask => ("build", null),
                    _ => ("macro", null),
                };
                await tasks.RunAsync(id, context: context);
                break;
        }

        bool shouldRun = state is State.Trusted or State.AcceptsWhenAsked;
        int expectedAsks = state switch
        {
            State.AcceptsWhenAsked or State.DeclinesWhenAsked => 1,
            State.DeclinedEarlierThisSession => StartsOnItsOwn(entry) ? 0 : 1,
            _ => 0,
        };

        bool ran = entry switch
        {
            Entry.ProjectSaveHook => File.Exists(Path.Combine(root, "hook.marker")),
            Entry.ProjectDetachedHook => await AppearsAsync(Path.Combine(root, "hook.marker"), shouldRun),
            Entry.ShellTaskStartedByTheUser or Entry.ShellTaskStartedByAHook => File.Exists(Path.Combine(root, "task.marker")),
            Entry.BuildTask => builds > 0,
            Entry.MacroTask => macros > 0,
            _ => approved == true,
        };
        string label = $"{entry} in a folder that is {state}";
        Assert.True(ran == shouldRun, $"{label}: ran={ran}, expected {shouldRun}");
        Assert.True(asked == expectedAsks, $"{label}: the user was asked {asked} time(s), expected {expectedAsks}");
        if (asked > 0) Assert.Equal(Path.GetFullPath(root), askedAbout);
        if (entry is Entry.ProjectSaveHook or Entry.ShellTaskStartedByTheUser or Entry.ShellTaskStartedByAHook)
            Assert.True(output.Sessions == (shouldRun ? 1 : 0), $"{label}: {output.Sessions} automation session(s) started");
        if (entry is Entry.BuildTask) Assert.True(builds <= 1);

        var remembered = trust.Get(sessionRoot ?? root);
        var expectedTrust = state is State.Trusted or State.AcceptsWhenAsked ? TrustState.Trusted : TrustState.Untrusted;
        if (state != State.NoFolderOpen) Assert.True(remembered == expectedTrust, $"{label}: the folder is now {remembered}");
        Assert.Equal(state is State.Trusted or State.AcceptsWhenAsked, new WorkspaceTrustService(Path.Combine(_dir, "trust.json")).Get(root) == TrustState.Trusted);
    }

    [Fact]
    public async Task The_users_own_hooks_run_in_an_untrusted_folder_but_the_projects_do_not()
    {
        var root = Path.Combine(_dir, "stranger");
        Directory.CreateDirectory(Path.Combine(root, ".thide"));
        File.WriteAllText(Path.Combine(root, ".thide", "hooks.json"),
            """{ "hooks": [ { "event": "FileSaved", "command": "echo ran > project.marker" } ] }""");
        int asked = 0;
        var trust = new WorkspaceTrustService(Path.Combine(_dir, "trust.json"), (_, _) => { asked++; return Task.FromResult(false); });
        var settings = new FakeSettings(AppSettings.Default with
        {
            Hooks = [new HookDefinition(HookEvent.FileSaved, "echo ran > user.marker")],
        });
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "get_RootPath" ? root : null);
        var hooks = new ScriptHookService(settings, session: session, trust: trust, output: new FakeOutput(), notifications: new FakeNotifications());

        await hooks.RunAsync(new HookContext(HookEvent.FileSaved, Path.Combine(root, "a.th"), root));

        Assert.True(File.Exists(Path.Combine(root, "user.marker")), "the user's own hook did not run");
        Assert.False(File.Exists(Path.Combine(root, "project.marker")), "the project's hook ran in a folder the user declined");
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Turning_hooks_off_runs_nothing_and_asks_nothing()
    {
        var root = Path.Combine(_dir, "quiet");
        Directory.CreateDirectory(Path.Combine(root, ".thide"));
        File.WriteAllText(Path.Combine(root, ".thide", "hooks.json"),
            """{ "hooks": [ { "event": "FileSaved", "command": "echo ran > project.marker" } ] }""");
        int asked = 0;
        var trust = new WorkspaceTrustService(Path.Combine(_dir, "trust.json"), (_, _) => { asked++; return Task.FromResult(true); });
        var settings = new FakeSettings(AppSettings.Default with
        {
            EnableScriptHooks = false,
            Hooks = [new HookDefinition(HookEvent.FileSaved, "echo ran > user.marker")],
        });
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "get_RootPath" ? root : null);

        await new ScriptHookService(settings, session: session, trust: trust, output: new FakeOutput()).RunAsync(
            new HookContext(HookEvent.FileSaved, Path.Combine(root, "a.th"), root));

        Assert.False(File.Exists(Path.Combine(root, "project.marker")));
        Assert.False(File.Exists(Path.Combine(root, "user.marker")));
        Assert.Equal(0, asked);
    }

    /// <summary>A detached hook is started and forgotten: wait for its marker when it should run.</summary>
    private static async Task<bool> AppearsAsync(string path, bool expected)
    {
        var deadline = DateTime.UtcNow + (expected ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(400));
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(25);
        }
        return File.Exists(path);
    }

    private sealed class FakeSettings(AppSettings current) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = current;
#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class FakeOutput : IAutomationOutput
    {
        private int _sessions;
        public int Sessions => _sessions;
#pragma warning disable CS0067
        public event EventHandler? SessionStarted;
#pragma warning restore CS0067
        public IAutomationSessionWriter BeginSession(string title, AutomationKind kind)
        {
            Interlocked.Increment(ref _sessions);
            return new Writer();
        }

        private sealed class Writer : IAutomationSessionWriter
        {
            public void Write(string text, bool isError = false) { }
            public void Complete(AutomationState state, int? exitCode = null) { }
            public void OnCancel(Action cancel) { }
        }
    }

    private sealed class FakeNotifications : INotificationService
    {
#pragma warning disable CS0067
        public event EventHandler<AppNotification>? Posted;
        public event EventHandler? UnreadChanged;
#pragma warning restore CS0067
        public ObservableCollection<AppNotification> History { get; } = [];
        public int UnreadCount => 0;
        public void Post(AppNotification notification) { }
        public void Info(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void Success(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void Warning(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void Error(string title, string message, string? actionLabel = null, Action? action = null) { }
        public void MarkAllRead() { }
        public void Clear() { }
    }
}
