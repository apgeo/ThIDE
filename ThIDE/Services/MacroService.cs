// Macros in the app: the built-in ones, the user's (app data) and the project's (its config folder,
// trusted folders only), run through the same in-process tool dispatcher as `therion-mcp macro`,
// against the live workspace (unsaved buffers included). Every run previews into the Automation
// panel and asks before applying.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Therion.Mcp;
using Therion.Mcp.Dispatch;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using ThIDE.Resources;
using ThIDE.ViewModels;

namespace ThIDE.Services;

public interface IMacroService
{
    /// <summary>All macros by name; project ones override the user's, which override the built-ins.</summary>
    IReadOnlyList<(MacroDefinition Macro, bool FromProject)> Macros();

    Task RunAsync(string name);
}

public sealed class MacroService : IMacroService
{
    private readonly IServiceProvider _app;
    private readonly IWorkspaceSession _session;
    private readonly IAppSettingsService _settings;
    private readonly IWorkspaceTrustService _trust;
    private readonly IAutomationOutput _output;
    private readonly INotificationService? _notifications;

    /// <summary>Asks the user for the macro's variables (null = cancelled) and whether to apply a preview.</summary>
    public Func<MacroDefinition, Task<Dictionary<string, string>?>>? AskVariables { get; set; }
    public Func<MacroDefinition, MacroRun, Task<bool>>? ConfirmApply { get; set; }

    public MacroService(IServiceProvider app, IWorkspaceSession session, IAppSettingsService settings, IWorkspaceTrustService trust,
        IAutomationOutput output, ITaskRunnerService? tasks = null, INotificationService? notifications = null)
    {
        _app = app;
        _session = session;
        _settings = settings;
        _trust = trust;
        _output = output;
        _notifications = notifications;
        if (tasks is not null) tasks.RunMacro = RunAsync;
    }

    private string? ProjectFolder() => _settings.Current.EnableProjectAutomation
        ? ProjectLocalConfig.PathFor(_session.RootPath, _settings.Current.ProjectConfigLocation, "macros") : null;

    public IReadOnlyList<(MacroDefinition Macro, bool FromProject)> Macros()
    {
        var all = new Dictionary<string, (MacroDefinition, bool)>(StringComparer.Ordinal);
        foreach (var m in MacroCatalog.BuiltIns) all[m.Name] = (m, false);
        foreach (var m in MacroCatalog.LoadFolder(ProjectSidecar.DirectoryFor("macros")).Macros) all[m.Name] = (m, false);
        var (project, errors) = MacroCatalog.LoadFolder(ProjectFolder());
        foreach (var m in project) all[m.Name] = (m, true);
        if (errors.Count > 0) _notifications?.Warning(Tr.Get("Macro_FileInvalid"), string.Join("\n", errors.Take(3)));
        return all.Values.OrderBy(m => m.Item1.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private ToolDispatcher Dispatcher()
    {
        var services = new ServiceCollection();
        var bridge = _app.GetRequiredService<IUiBridge>();
        services.AddSingleton(bridge);
        services.AddSingleton(_app.GetRequiredService<IExternalToolLocator>());
        services.AddSingleton(_app.GetRequiredService<ITherionCompiler>());
        services.AddSingleton<IWorkspaceHost>(_ => new LiveWorkspaceHost(_session, _app.GetRequiredService<IUnsavedBufferProvider>(), bridge));
        services.AddToolServices();
        return new ToolDispatcher(services.BuildServiceProvider(), McpProfile.Full, includeUi: true);
    }

    public async Task RunAsync(string name)
    {
        var entry = Macros().FirstOrDefault(m => m.Macro.Name == name);
        if (entry.Macro is not { } macro) { _notifications?.Warning(Tr.Get("Macro_Title"), string.Format(Tr.Get("Macro_Unknown"), name)); return; }
        if (entry.FromProject && (_session.RootPath is not { } root || !await _trust.EnsureTrustedAsync(root, Tr.Get("Trust_ReasonMacros"))))
        {
            _notifications?.Warning(Tr.Get("Trust_Skipped"), string.Format(Tr.Get("Trust_SkippedDetail"), macro.Title));
            return;
        }

        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_session.RootPath is { } r) vars["workspaceRoot"] = r;
        if (macro.Vars.Count > 0)
        {
            if (AskVariables is null || await AskVariables(macro) is not { } given) return;
            foreach (var (k, v) in given) vars[k] = v;
        }

        var runner = new MacroRunner(Dispatcher());
        var problems = runner.Check(macro, vars);
        var session = _output.BeginSession($"{Tr.Get("Macro_Title")}: {macro.Title}", AutomationKind.Macro);
        if (problems.Count > 0)
        {
            foreach (var p in problems) session.Write(p, isError: true);
            session.Complete(AutomationState.Failed);
            return;
        }

        var progress = new Therion.Build.Automation.SyncProgress<StepResult>(s =>
            session.Write($"{s.StepId}: {s.Tool} — {s.Status}: {s.Summary}", s.Status == StepStatus.Failed));
        session.Write(Tr.Get("Macro_Previewing"));
        // Off the UI thread: the live workspace host marshals onto it for each tool call.
        var preview = await Task.Run(() => runner.RunAsync(macro, vars, apply: false, progress: progress));
        if (preview.Failed || ConfirmApply is null || !await ConfirmApply(macro, preview))
        {
            session.Complete(preview.Failed ? AutomationState.Failed : AutomationState.Cancelled);
            return;
        }
        session.Write(Tr.Get("Macro_Applying"));
        var run = await Task.Run(() => runner.RunAsync(macro, vars, apply: true, preview, progress));
        session.Complete(run.Failed ? AutomationState.Failed : AutomationState.Succeeded);
    }
}
