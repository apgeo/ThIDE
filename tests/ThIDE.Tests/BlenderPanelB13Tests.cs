// BA-B13 ViewModel tests: notifications wiring, job history, open-in-Blender-GUI, and the
// failure-kind → localized message / not-found affordance. Fakes for the notification, shell,
// GUI-launcher, render, and source seams (no Blender, no app shell).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Sources;
using Therion.Build;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class BlenderPanelB13Tests
{
    private sealed class FakeRender : IBlenderRenderService
    {
        public RenderResult Result { get; set; } = new()
        {
            Succeeded = true,
            OutputPaths = ImmutableArray.Create("/out/cave.mp4"),
            Device = "CPU",
            JobLogPath = "/out/job.log",
        };
        public Task<RenderResult> RenderAsync(SceneSpec spec, RenderSource source, IProgress<RenderProgress>? p = null, CancellationToken ct = default)
            => Task.FromResult(Result);
        public bool LastInteractive { get; private set; }
        public Task<string> ExportScriptAsync(SceneSpec spec, RenderSource source, string outputDir, IProgress<RenderProgress>? p = null, CancellationToken ct = default, bool interactive = false)
        {
            LastInteractive = interactive;
            return Task.FromResult(System.IO.Path.Combine(outputDir, "render.py"));
        }
    }

    private sealed class FakeSources : IBlenderSourceProvider
    {
        public IReadOnlyList<ModelArtifact> DiscoverArtifacts() => [];
        public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default)
            => Task.FromResult(new RenderSource(new ResolvedModelSource { Path = "m.lox", Format = CaveSourceFormat.Lox, Kind = ModelSourceKind.ExternalFile }, []));
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<(NotificationKind Kind, string Message, bool HasAction)> Items { get; } = [];
        public event EventHandler<AppNotification>? Posted { add { } remove { } }
        public event EventHandler? UnreadChanged { add { } remove { } }
        public ObservableCollection<AppNotification> History { get; } = [];
        public int UnreadCount => 0;
        public void Post(AppNotification n) => Items.Add((n.Kind, n.Message, n.HasAction));
        public void Info(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Info, a, ac));
        public void Success(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Success, a, ac));
        public void Warning(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Warning, a, ac));
        public void Error(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Error, a, ac));
        public void MarkAllRead() { }
        public void Clear() { }
    }

    private sealed class FakeShell : IShellOpener
    {
        public List<string> Opened { get; } = [];
        public bool Open(string path) { Opened.Add(path); return true; }
        public bool RevealInFileManager(string path) { Opened.Add(path); return true; }
    }

    private sealed class FakeGui : IBlenderGuiLauncher
    {
        public bool Result { get; set; } = true;
        public string? Launched { get; private set; }
        public bool Launch(string scriptPath) { Launched = scriptPath; return Result; }
    }

    private static BlenderAnimationViewModel Vm(FakeRender? render = null, FakeNotifications? notify = null, FakeShell? shell = null, FakeGui? gui = null)
    {
        var vm = new BlenderAnimationViewModel(render ?? new FakeRender(), new FakeSources(), null, notify, shell, gui);
        vm.UseExternalFile = true;
        vm.ExternalFilePath = "m.lox";
        return vm;
    }

    [Fact]
    public async Task Render_Success_RecordsJob_AndNotifies()
    {
        var notify = new FakeNotifications();
        var vm = Vm(notify: notify, shell: new FakeShell());

        await vm.RenderCommand.ExecuteAsync(null);

        var job = Assert.Single(vm.Jobs);
        Assert.True(job.Succeeded);
        Assert.Contains("/out/cave.mp4", job.Outputs);
        Assert.Contains(notify.Items, i => i.Kind == NotificationKind.Success && i.HasAction); // "Open folder" action
    }

    [Fact]
    public async Task Render_BlenderNotFound_SetsMissingFlag_AndLocalizedError_AndErrorNotification()
    {
        var render = new FakeRender { Result = new RenderResult { Succeeded = false, FailureKind = RenderFailureKind.BlenderNotFound, ErrorMessage = "raw" } };
        var notify = new FakeNotifications();
        var vm = Vm(render, notify);

        await vm.RenderCommand.ExecuteAsync(null);

        Assert.True(vm.BlenderMissing);
        Assert.Equal(ThIDE.Resources.Tr.Get("Blender_Fail_NotFound"), vm.LastError); // not the raw message
        Assert.Contains(notify.Items, i => i.Kind == NotificationKind.Error);
        Assert.False(vm.Jobs[0].Succeeded);
    }

    [Fact]
    public async Task OpenJobLog_OpensTheLastLog()
    {
        var shell = new FakeShell();
        var vm = Vm(shell: shell);
        await vm.RenderCommand.ExecuteAsync(null);

        Assert.True(vm.OpenJobLogCommand.CanExecute(null));
        vm.OpenJobLogCommand.Execute(null);
        Assert.Contains("/out/job.log", shell.Opened);
    }

    [Fact]
    public async Task OpenInBlenderGui_ExportsAndLaunches()
    {
        var gui = new FakeGui();
        var render = new FakeRender();
        var vm = Vm(render: render, gui: gui);

        Assert.True(vm.OpenInBlenderGuiCommand.CanExecute(null));
        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);

        Assert.EndsWith("render.py", gui.Launched);
        Assert.False(vm.BlenderMissing);
        Assert.True(render.LastInteractive); // GUI export must request the interactive (no-render) script
    }

    [Fact]
    public async Task OpenInBlenderGui_LaunchFails_ShowsMissing()
    {
        var gui = new FakeGui { Result = false };
        var vm = Vm(gui: gui);

        await vm.OpenInBlenderGuiCommand.ExecuteAsync(null);

        Assert.True(vm.BlenderMissing);
        Assert.Equal(ThIDE.Resources.Tr.Get("Blender_Fail_NotFound"), vm.LastError);
    }

    [Fact]
    public void OpenInBlenderGui_Disabled_WithoutLauncher()
    {
        var vm = Vm(gui: null); // no launcher injected
        Assert.False(vm.OpenInBlenderGuiCommand.CanExecute(null));
    }

    [Fact]
    public void CopyJob_HandlesEntryAndNull()
    {
        var vm = Vm();
        var entry = new JobHistoryEntry("cave", true, "Done", ["/out/cave.mp4"], "/out/job.log", System.TimeSpan.Zero);
        // The clipboard is a headless no-op; the command must run cleanly for a real entry and null.
        vm.CopyJobCommand.Execute(entry);
        vm.CopyJobCommand.Execute(null);
    }
}
