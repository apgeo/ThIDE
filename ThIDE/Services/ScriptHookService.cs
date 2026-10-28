// Hooks: commands run when a workspace opens, a file is opened or saved, or a build starts or
// ends. The three original single-command settings still run detached, exactly as before; the
// user's hook list and the project's hooks.json run through ProcessRunner with their output in the
// Automation panel, a timeout and the THIDE_* environment. Project hooks run only in a trusted folder.
// A failing hook never blocks the app.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Build.Automation;
using Therion.Workspace;
using ThIDE.Resources;
using ThIDE.ViewModels;

namespace ThIDE.Services;

public enum ScriptHookEvent { Open, Save, Build }

public interface IScriptHookService
{
    /// <summary>The original entry point (open / save / build); runs every hook for that event.</summary>
    void Run(ScriptHookEvent ev, string? filePath = null);

    /// <summary>Runs every hook for an event in the background, in order.</summary>
    void Fire(HookContext context);

    /// <summary>Runs every hook for an event and waits for the attached ones.</summary>
    Task RunAsync(HookContext context, CancellationToken ct = default);

    /// <summary>Runs a task by id, with the hook's context, for hooks that name one (set by the task runner).</summary>
    Func<string, HookContext, Task>? RunTask { get; set; }
}

public sealed class ScriptHookService : IScriptHookService
{
    private readonly IAppSettingsService _settings;
    private readonly ILogService? _log;
    private readonly IWorkspaceSession? _session;
    private readonly IWorkspaceTrustService? _trust;
    private readonly IAutomationOutput? _output;
    private readonly INotificationService? _notifications;
    private readonly HashSet<string> _declined = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _order = new(1, 1);

    public ScriptHookService(IAppSettingsService settings, ILogService? log = null, IWorkspaceSession? session = null,
        IWorkspaceTrustService? trust = null, IAutomationOutput? output = null, INotificationService? notifications = null)
    {
        _settings = settings;
        _log = log;
        _session = session;
        _trust = trust;
        _output = output;
        _notifications = notifications;
    }

    public Func<string, HookContext, Task>? RunTask { get; set; }
    private readonly SemaphoreSlim _asking = new(1, 1);

    public void Run(ScriptHookEvent ev, string? filePath = null)
    {
        var hookEvent = ev switch
        {
            ScriptHookEvent.Open => HookEvent.FileOpened,
            ScriptHookEvent.Save => HookEvent.FileSaved,
            _ => HookEvent.BuildStarted,
        };
        Fire(Context(hookEvent, filePath, ev == ScriptHookEvent.Build ? filePath : null));
    }

    /// <summary>A context with the session's root and active thconfig filled in.</summary>
    public HookContext Context(HookEvent ev, string? file = null, string? entry = null, BuildOutcome? outcome = null) =>
        new(ev, file, _session?.RootPath, _session?.ActiveThconfig?.FullPath, entry, outcome);

    public void Fire(HookContext context)
    {
        if (!_settings.Current.EnableScriptHooks) return;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(context).ConfigureAwait(false); }
            catch (Exception ex) { _log?.Warning($"{context.Event} hooks failed: {ex.Message}"); }
        });
    }

    public async Task RunAsync(HookContext context, CancellationToken ct = default)
    {
        var s = _settings.Current;
        if (!s.EnableScriptHooks) return;
        var hooks = HookCatalog.For(HookCatalog.Merge(s.HookOnOpen, s.HookOnSave, s.HookOnBuild, s.Hooks, ProjectHooks(s)), context.Event)
            .Where(h => !string.IsNullOrWhiteSpace(h.Command) || !string.IsNullOrWhiteSpace(h.Task))
            .ToList();
        if (hooks.Count == 0) return;

        if (hooks.Any(h => h.Source == HookSource.Project) && !await ProjectAllowedAsync(context.WorkspaceRoot).ConfigureAwait(false))
            hooks.RemoveAll(h => h.Source == HookSource.Project);

        await _order.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var hook in hooks)
                await RunOneAsync(hook, context, ct).ConfigureAwait(false);
        }
        finally { _order.Release(); }
    }

    private IReadOnlyList<HookDefinition> ProjectHooks(AppSettings s)
    {
        if (!s.EnableProjectAutomation || ProjectLocalConfig.PathFor(_session?.RootPath, s.ProjectConfigLocation, "hooks.json") is not { } path
            || !File.Exists(path)) return [];
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        var (hooks, error) = HookCatalog.LoadProject(json);
        if (error is not null) _log?.Warning(error);
        return hooks;
    }

    // Asks once per folder per session; after "not now" the project's hooks are skipped quietly
    // (one toast, with a Trust… action).
    private async Task<bool> ProjectAllowedAsync(string? root)
    {
        if (_trust is null || root is null) return false;
        if (_trust.Get(root) == TrustState.Trusted) return true;
        // One question at a time: Save All fires a hook per file, and only the first may ask.
        await _asking.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_trust.Get(root) == TrustState.Trusted) return true;
            lock (_declined) if (_declined.Contains(root)) return false;
            if (await _trust.EnsureTrustedAsync(root, Tr.Get("Trust_ReasonHooks")).ConfigureAwait(false)) return true;
            lock (_declined) _declined.Add(root);
        }
        finally { _asking.Release(); }
        _notifications?.Warning(Tr.Get("Trust_Skipped"), string.Format(Tr.Get("Trust_SkippedDetail"), "hooks.json"),
            Tr.Get("Trust_TrustAction"), () => _trust.Trust(root));
        return false;
    }

    private async Task RunOneAsync(HookDefinition hook, HookContext ctx, CancellationToken ct)
    {
        if (hook.Task is { Length: > 0 } taskId)
        {
            // Tasks may build or open windows: they run on the UI thread.
            if (RunTask is { } run) await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => run(taskId, ctx));
            return;
        }
        var command = HookVariables.Expand(hook.Command!, ctx, v => ShellQuote.ForArgument(v));
        var cwd = hook.WorkingDirectory is { Length: > 0 } wd
            ? HookVariables.Expand(wd, ctx, v => v)
            : TryDir(ctx.File) ?? ctx.WorkspaceRoot;
        var env = HookVariables.Environment(ctx);
        foreach (var (k, v) in hook.Env ?? new Dictionary<string, string>()) env[k] = HookVariables.Expand(v, ctx, x => x);
        var spec = new ProcessSpec(command, [], cwd, env, TimeSpan.FromSeconds(Math.Max(1, hook.TimeoutSeconds)), UseShell: true);

        if (hook.Detached)
        {
            try
            {
                var psi = ProcessRunner.StartInfo(spec);
                psi.RedirectStandardOutput = psi.RedirectStandardError = psi.RedirectStandardInput = false;
                Process.Start(psi)?.Dispose();   // detached — nothing waits for it
                _log?.Verbose($"Ran {ctx.Event} hook: {command}");
            }
            catch (Exception ex) { _log?.Warning($"{ctx.Event} hook failed: {ex.Message}"); }
            return;
        }

        var session = _output?.BeginSession($"{Tr.Get("Automation_Hook")}: {ctx.Event}", AutomationKind.Hook);
        session?.Write("$ " + command);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        session?.OnCancel(cts.Cancel);
        var outcome = await ProcessRunner.RunAsync(spec, new SyncProgress<ProcessLine>(l => session?.Write(l.Text, l.IsError)), cts.Token).ConfigureAwait(false);
        if (outcome.StartError is { } err) session?.Write(err, isError: true);
        session?.Complete(outcome.Succeeded ? AutomationState.Succeeded : outcome.Cancelled ? AutomationState.Cancelled : AutomationState.Failed, outcome.ExitCode);
        if (outcome.TimedOut)
            _notifications?.Warning(Tr.Get("Automation_HookTimedOut"), string.Format(Tr.Get("Automation_HookTimedOutDetail"), ctx.Event, hook.TimeoutSeconds));
        _log?.Verbose($"{ctx.Event} hook exited {outcome.ExitCode}: {command}");
    }

    private static string? TryDir(string? filePath)
    {
        try { return string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(Path.GetFullPath(filePath)); }
        catch { return null; }
    }
}
