// Runs a project's tasks (.thide/tasks.json): shell commands into the Automation panel (their
// output turned into diagnostics by a problem matcher), built-in builds, and macros. Tasks are
// project-defined, so they run only in a trusted folder.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Build.Automation;
using Therion.Core;
using Therion.Workspace;
using ThIDE.Resources;
using ThIDE.ViewModels;

namespace ThIDE.Services;

public interface ITaskRunnerService
{
    /// <summary>The project's tasks, read fresh (the file is small and may have just been edited).</summary>
    IReadOnlyList<TaskDefinition> Tasks();

    /// <param name="context">The hook that started it, when one did (its file and event reach the task).</param>
    Task<bool> RunAsync(string id, CancellationToken ct = default, HookContext? context = null);

    /// <summary>Builds entries (null = the active thconfig): set by the shell.</summary>
    Func<IReadOnlyList<string>?, bool, Task>? RunBuild { get; set; }

    /// <summary>Runs a macro by id: set by the macro runner.</summary>
    Func<string, Task>? RunMacro { get; set; }

    /// <summary>Shows a task's diagnostics (merged into the Diagnostics panel): set by the shell.</summary>
    Action<ImmutableArray<Diagnostic>>? PublishDiagnostics { get; set; }
}

public sealed class TaskRunnerService : ITaskRunnerService
{
    private readonly IAppSettingsService _settings;
    private readonly IWorkspaceSession _session;
    private readonly IWorkspaceTrustService _trust;
    private readonly IAutomationOutput _output;
    private readonly INotificationService? _notifications;
    private readonly ILogService? _log;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public TaskRunnerService(IAppSettingsService settings, IWorkspaceSession session, IWorkspaceTrustService trust,
        IAutomationOutput output, IScriptHookService? hooks = null, INotificationService? notifications = null, ILogService? log = null)
    {
        _settings = settings;
        _session = session;
        _trust = trust;
        _output = output;
        _notifications = notifications;
        _log = log;
        if (hooks is not null) hooks.RunTask = (id, ctx) => RunAsync(id, context: ctx);
    }

    public Func<IReadOnlyList<string>?, bool, Task>? RunBuild { get; set; }
    public Func<string, Task>? RunMacro { get; set; }
    public Action<ImmutableArray<Diagnostic>>? PublishDiagnostics { get; set; }

    public IReadOnlyList<TaskDefinition> Tasks()
    {
        var s = _settings.Current;
        if (!s.EnableProjectAutomation || ProjectLocalConfig.PathFor(_session.RootPath, s.ProjectConfigLocation, "tasks.json") is not { } path
            || !File.Exists(path)) return [];
        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        var (tasks, errors) = TaskCatalog.Load(json);
        var fresh = errors.Where(e => _reported.Add(e)).ToList();
        if (fresh.Count > 0)
        {
            foreach (var e in fresh) _log?.Warning(e);
            _notifications?.Warning(Tr.Get("Task_FileInvalid"), string.Join("\n", fresh.Take(3)));
        }
        return tasks;
    }

    public async Task<bool> RunAsync(string id, CancellationToken ct = default, HookContext? context = null)
    {
        var task = Tasks().FirstOrDefault(t => t.Id == id);
        if (task is null) { _notifications?.Warning(Tr.Get("Task_Unknown"), id); return false; }
        // Started by a hook, the task asks like the hooks do: once, not on every save.
        var root = _session.RootPath;
        var (trusted, asked) = root is null ? (false, true) : context is null
            ? (await _trust.EnsureTrustedAsync(root, Tr.Get("Trust_ReasonTasks")).ConfigureAwait(true), true)
            : await _trust.EnsureTrustedOnceAsync(root, Tr.Get("Trust_ReasonTasks")).ConfigureAwait(true);
        if (!trusted || root is null)
        {
            if (asked) _notifications?.Warning(Tr.Get("Trust_Skipped"), string.Format(Tr.Get("Trust_SkippedDetail"), task.Title));
            return false;
        }

        switch (task.Type)
        {
            case TaskKind.Builtin:
                if (RunBuild is null) return false;
                await RunBuild(task.Entries?.Select(e => Path.GetFullPath(Path.Combine(root, e))).ToList(), task.Builtin == "batch-build").ConfigureAwait(true);
                return true;
            case TaskKind.Macro:
                if (RunMacro is null) return false;
                await RunMacro(task.Macro!).ConfigureAwait(true);
                return true;
        }

        var ctx = (context ?? new HookContext(HookEvent.BuildStarted)) with { WorkspaceRoot = root, ActiveThconfig = _session.ActiveThconfig?.FullPath };
        var spec = TaskCatalog.Spec(task, ctx);
        var session = _output.BeginSession($"{Tr.Get("Automation_Task")}: {task.Title}", AutomationKind.Task);
        session.Write("$ " + (spec.UseShell ? spec.FileName : string.Join(' ', new[] { spec.FileName }.Concat(spec.Args))));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        session.OnCancel(cts.Cancel);

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var lines = new SyncProgress<ProcessLine>(l =>
        {
            session.Write(l.Text, l.IsError);
            if (ProblemMatchers.Match(task.ProblemMatcher, l.Text, l.IsError, spec.WorkingDirectory, task.Id) is { } d)
                lock (diagnostics) diagnostics.Add(d);
        });
        var outcome = await ProcessRunner.RunAsync(spec, lines, cts.Token).ConfigureAwait(true);
        if (outcome.StartError is { } err) session.Write(err, isError: true);
        session.Complete(outcome.Succeeded ? AutomationState.Succeeded : outcome.Cancelled ? AutomationState.Cancelled : AutomationState.Failed, outcome.ExitCode);
        if (task.ProblemMatcher is not null) lock (diagnostics) PublishDiagnostics?.Invoke(diagnostics.ToImmutable());
        return outcome.Succeeded;
    }
}
