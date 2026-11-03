// Project hooks (.thide/hooks.json) run only in a trusted folder. These tests pin two parts of that
// promise: the "Trust…" action on the skipped-hooks toast asks before it trusts, and the hooks that
// run belong to the folder whose trust was checked.

using System.Collections.ObjectModel;
using System.Reflection;
using Therion.Build.Automation;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class ProjectHookTrustTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("thide-hook-trust-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeSettings : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;
        public event EventHandler? Changed;
        public void Save(AppSettings settings) { Current = settings; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeNotifications : INotificationService
    {
        public readonly List<AppNotification> Posted = [];
#pragma warning disable CS0067
        event EventHandler<AppNotification>? INotificationService.Posted { add { } remove { } }
        public event EventHandler? UnreadChanged;
#pragma warning restore CS0067
        public ObservableCollection<AppNotification> History { get; } = [];
        public int UnreadCount => 0;
        public void Post(AppNotification notification) => Posted.Add(notification);
        public void Info(string title, string message, string? actionLabel = null, Action? action = null)
            => Post(new AppNotification(title, message, NotificationKind.Info, actionLabel, action));
        public void Success(string title, string message, string? actionLabel = null, Action? action = null)
            => Post(new AppNotification(title, message, NotificationKind.Success, actionLabel, action));
        public void Warning(string title, string message, string? actionLabel = null, Action? action = null)
            => Post(new AppNotification(title, message, NotificationKind.Warning, actionLabel, action));
        public void Error(string title, string message, string? actionLabel = null, Action? action = null)
            => Post(new AppNotification(title, message, NotificationKind.Error, actionLabel, action));
        public void MarkAllRead() { }
        public void Clear() { }
    }

    /// <summary>Records every command line a hook session announces.</summary>
    private sealed class FakeOutput : IAutomationOutput
    {
        public readonly List<string> Lines = [];
#pragma warning disable CS0067
        public event EventHandler? SessionStarted;
#pragma warning restore CS0067
        public IAutomationSessionWriter BeginSession(string title, AutomationKind kind) => new Writer(Lines);

        private sealed class Writer(List<string> lines) : IAutomationSessionWriter
        {
            public void Write(string text, bool isError = false) { lock (lines) lines.Add(text); }
            public void Complete(AutomationState state, int? exitCode = null) { }
            public void OnCancel(Action cancel) { }
        }
    }

    private static IWorkspaceSession SessionAt(string root) =>
        WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "get_RootPath" ? root : null);

    private string Folder(string name, string? hookCommand)
    {
        var root = Path.Combine(_dir, name);
        Directory.CreateDirectory(root);
        if (hookCommand is not null)
        {
            Directory.CreateDirectory(Path.Combine(root, ".thide"));
            File.WriteAllText(Path.Combine(root, ".thide", "hooks.json"),
                $$"""{ "hooks": [ { "event": "FileSaved", "command": "{{hookCommand}}" } ] }""");
        }
        return root;
    }

    [Fact]
    public async Task The_trust_action_on_the_skipped_hooks_toast_asks_before_it_trusts()
    {
        var root = Folder("cave", "echo saved");
        var trust = new WorkspaceTrustService(Path.Combine(_dir, "trust.json"), (_, _) => Task.FromResult(false));
        var notifications = new FakeNotifications();
        var hooks = new ScriptHookService(new FakeSettings(), session: SessionAt(root), trust: trust, notifications: notifications);

        await hooks.RunAsync(new HookContext(HookEvent.FileSaved, Path.Combine(root, "a.th"), root));
        var toast = Assert.Single(notifications.Posted);
        Assert.NotNull(toast.Action);

        // The user declined once already; the action's label ends in an ellipsis, so it must ask again.
        toast.Action!.Invoke();

        Assert.Equal(TrustState.Untrusted, trust.Get(root));
    }

    [Fact]
    public async Task Project_hooks_come_from_the_folder_whose_trust_was_checked()
    {
        var trusted = Folder("trusted", hookCommand: null);
        var stranger = Folder("stranger", "echo hooked-from-stranger");
        var trust = new WorkspaceTrustService(Path.Combine(_dir, "trust.json"), (_, _) => Task.FromResult(false));
        trust.Trust(trusted);
        var output = new FakeOutput();
        // The session has moved on to the untrusted folder, but the hook was queued for the trusted one.
        var hooks = new ScriptHookService(new FakeSettings(), session: SessionAt(stranger), trust: trust, output: output);

        await hooks.RunAsync(new HookContext(HookEvent.FileSaved, Path.Combine(trusted, "a.th"), trusted));

        Assert.Equal(TrustState.Untrusted, trust.Get(stranger));
        Assert.DoesNotContain(output.Lines, l => l.Contains("hooked-from-stranger"));
    }
}
