// Implementation Plan �7.1 � DI composition root.
// Wires Microsoft.Extensions.* and Therion.* concrete implementations.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Therion.Build;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Semantics.UserRules;
using Therion.Syntax;
using Therion.Workspace;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE;

internal static class AppServices
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("AppServices not initialized.");

    /// <summary>
    /// Loads the optional user semantic-rule config from <c>%AppData%/ThIDE/rules.json</c>
    /// (XDG fallback on POSIX). Returns an empty config when the file is absent or invalid.
    /// </summary>
    private static SemanticRuleConfig LoadRuleConfig()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            var path = Path.Combine(appData, "ThIDE", "rules.json");
            return File.Exists(path) ? SemanticRuleConfig.Load(File.ReadAllText(path)) : SemanticRuleConfig.Empty;
        }
        catch
        {
            return SemanticRuleConfig.Empty;
        }
    }

    /// <summary>Reads persisted settings before the container is built (plugin gate).</summary>
    private static AppSettings LoadInitialSettings()
    {
        try { return new AppSettingsService().Current; }
        catch { return AppSettings.Default; }
    }

    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Logging (�13 M1 � Console + Debug providers; rolling file provider added in M5b).
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddSimpleConsole(o => o.SingleLine = true);
            b.AddDebug();
        });

        // Localization (§7.6). The Strings marker type lives in the ThIDE.Resources
        // namespace and the .resx files sit in Resources/, so their manifest base name is
        // already "ThIDE.Resources.Strings". Setting ResourcesPath="Resources" made the
        // localizer look for "ThIDE.ResourcesResources.Strings" (doubled segment), so it
        // never found any resource and every label fell back to its English literal — which is
        // why switching to Romanian did nothing (#9). Leaving ResourcesPath empty resolves the
        // base name to the type's full name, matching the embedded resources.
        services.AddLocalization();
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IGlobalHotkeyService>(_ => GlobalHotkeyServiceFactory.Create());

        // Workspace primitives (�6).
        services.AddSingleton(WorkspaceOptions.FromEnvironment());
        services.AddSingleton<IThconfigSniffer, ThconfigSniffer>();
        services.AddSingleton<IProjectEntryPointResolver, ProjectEntryPointResolver>();

        // Parse caches (�4.5 / �M5 / Post-M6 D � disk cache is opt-in and format-selectable).
        services.AddSingleton<IDiskParseCache>(sp =>
        {
            var opts = sp.GetRequiredService<WorkspaceOptions>();
            if (opts.DisableDiskCache) return NullDiskParseCache.Instance;
            return opts.DiskCacheFormat == DiskCacheFormat.MessagePack
                ? new MessagePackDiskParseCache()
                : new JsonDiskParseCache();
        });
        services.AddSingleton<IParseCache>(sp =>
        {
            var opts = sp.GetRequiredService<WorkspaceOptions>();
            var l1 = new InMemoryParseCache();
            if (opts.DisableDiskCache) return l1;
            return new TieredParseCache(l1, sp.GetRequiredService<IDiskParseCache>());
        });

        // Semantics (�5 / �M6) � uses AddTherionSemantics() so rule plugins flow in via ISemanticRule.
        // a user rules.json (next to settings.json) can disable built-in rules and add
        // naming-convention lints. Missing/invalid config falls back to the default (all rules on).
        var ruleConfig = LoadRuleConfig();
        services.AddTherionSemantics(ruleConfig);
        services.AddTherionBuiltinSemanticRules();

        // load external plugin semantic rules from the plugins folder (gated by the
        // EnablePlugins setting, default on; disable for big projects). Registered as ISemanticRule
        // singletons so the rule runner resolves them alongside the built-ins.
        if (LoadInitialSettings().EnablePlugins)
            foreach (var rule in PluginLoader.LoadSemanticRules(PluginLoader.DefaultPluginDirectory()))
                services.AddSingleton<ISemanticRule>(rule);

        // Syntax extensibility (�4.4) � command handlers register via ICommandHandler.
        services.AddTherionCommands();

        // Build / external tools (�9bis).
        services.AddSingleton<IExternalToolPathOverrides, JsonExternalToolPathOverrides>();
        services.AddSingleton<IExternalToolLocator>(sp =>
            new ExternalToolLocator(sp.GetRequiredService<IExternalToolPathOverrides>()));
        services.AddSingleton<ITherionOutputParser, HeuristicTherionOutputParser>();
        services.AddSingleton<IOutputArtifactCollector, OutputArtifactCollector>();
        services.AddSingleton<IOutputArtifactCache, JsonOutputArtifactCache>();
        services.AddSingleton<ICompileGate, CompileGate>();
        services.AddSingleton<IShellOpener, ShellOpener>();
        services.AddSingleton<ITherionCompiler, TherionCompiler>();

        // "Edit with Mapiah" — detect + launch the external .th2 sketch editor.
        services.AddSingleton<IMapiahService, MapiahService>();

        // Single-root workspace session (re-org #1�#9) � owns root, active thconfig,
        // the shared object graph and the recursive filesystem watcher.
        services.AddSingleton<IWorkspaceSession, WorkspaceSessionService>();

        // Native OS shell icons for the file-explorer view.
        services.AddSingleton<IFileIconProvider, FileIconProvider>();

        // Destructive filesystem ops (delete → recycle bin / trash). The factory picks
        // the Windows or Unix implementation, isolating per-OS code from the views.
        services.AddSingleton<IFileOperations>(_ => FileOperationsFactory.Create());

        // Native OS shell context menu (Windows only today; no-op elsewhere) — keeps the
        // shell32 interop out of the views and lets the UI hide the entry where unsupported.
        services.AddSingleton<INativeContextMenuService>(_ => NativeContextMenuServiceFactory.Create());

        // Active-document host (�7.3).
        services.AddSingleton<IDocumentService, DocumentService>();

        // Quick-open (Ctrl+P go-to-file) data source (#3).
        services.AddSingleton<QuickOpenProvider>();

        // Bookmarks (B3).
        services.AddSingleton<IBookmarksService, BookmarksService>();

        // thbook documentation lookup (#6) — term→page map + open-at-page in default viewer.
        // PdfPageOpener detects the default PDF app and adapts the page syntax (#2).
        services.AddSingleton<IPdfPageOpener, PdfPageOpener>();
        services.AddSingleton<IThbookDocumentationService, ThbookDocumentationService>();

        // ThIDE User Guide (Help ▸ User Guide) — opens the bundled PDF (built by
        // build/build-user-guide.ps1), falling back to the on-disk Markdown or the online docs.
        services.AddSingleton<IUserGuideService, UserGuideService>();

        // Keyboard shortcuts (�9bis.5a / Decision #29).
        services.AddSingleton<IKeyboardShortcutService, JsonKeyboardShortcutService>();

        // Window-bounds persistence (dock layout is rebuilt fresh each launch).
        services.AddSingleton<ILayoutService, JsonLayoutService>();

        // Application preferences + session restore.
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<ILogService, LogService>();   // #3 in-app activity log
        services.AddSingleton<INotificationService, NotificationService>();   // toast/bell center
        services.AddSingleton<ICrashRecoveryService, CrashRecoveryService>(); // safe-mode + buffer recovery
        services.AddSingleton<IWorkspaceSymbolIndexStore, WorkspaceSymbolIndexStore>(); // persistent symbol index
        services.AddSingleton<ITelemetryService, LocalTelemetryService>();    // opt-in local telemetry/crash reports
        services.AddSingleton<IScriptHookService, ScriptHookService>();       // scripting/macro hooks
        services.AddSingleton<IMapRenderService, MapRenderService>();   // in-app rendering
        services.AddSingleton<ICaveview3DAssetHost, Caveview3DAssetHost>(); // loopback asset server
        services.AddSingleton<IStructuralPlotAssetHost, StructuralPlotAssetHost>(); // plot loopback server
        services.AddSingleton<IStationSourceResolver, StationSourceResolver>(); // label → .th span
        services.AddSingleton<IFileAssociationService>(_ => FileAssociationServiceFactory.Create()); // Task 5: OS file associations

        // Shared content ViewModels — singletons so the same instance flows to both
        // the dockable tool wrapper (shown in the UI) and the shell (event wiring).
        services.AddSingleton<ObjectBrowserViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<BuildViewModel>();
        services.AddSingleton<WorkspaceExplorerViewModel>();
        services.AddSingleton<XviReferencesViewModel>();
        services.AddSingleton<OutlineViewModel>();   // document outline content VM
        services.AddSingleton<SurveyTreeViewModel>();         // logical survey tree
        services.AddSingleton<ProjectDashboardViewModel>();   // project dashboard
        services.AddSingleton<ProjectAuditViewModel>();       // orphan/dead-file audit
        services.AddSingleton<DataAnalyticsViewModel>();      // analytics
        services.AddSingleton<ILeadStatusStore, LeadStatusStore>();   // lifecycle status
        services.AddSingleton<LeadsViewModel>();              // leads register
        services.AddSingleton<TodoScanViewModel>();           // TODO/FIXME/QM aggregator
        services.AddSingleton<IProjectMetadataStore, ProjectMetadataStore>(); // metadata sidecar
        services.AddSingleton<ProjectMetadataViewModel>();    // project metadata editor
        services.AddSingleton<MediaManagerViewModel>();       // media manager
        services.AddSingleton<LogViewModel>();                // #3 activity log content VM
        services.AddSingleton<LivePreviewViewModel>();        // live centreline preview
        services.AddSingleton<MapViewerViewModel>();          // in-app map viewer
        services.AddSingleton<Model3DViewerViewModel>();      // embedded 3D model viewer
        services.AddSingleton<StructuralGeologyViewModel>();  // plane strike/dip calculator
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<FileAssociationsViewModel>();   // Task 5: Preferences ▸ File Associations tab
        services.AddSingleton<KeyboardShortcutsViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ReplaceInFilesViewModel>();

        // Dock tool wrappers + the VS-classic layout factory.
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<ViewModels.Docking.WelcomeToolViewModel>();
        services.AddSingleton<ViewModels.Docking.WorkspaceExplorerToolViewModel>();
        services.AddSingleton<ViewModels.Docking.ObjectBrowserToolViewModel>();
        services.AddSingleton<ViewModels.Docking.DiagnosticsToolViewModel>();
        services.AddSingleton<ViewModels.Docking.CompilerOutputToolViewModel>();
        services.AddSingleton<ViewModels.Docking.GeneratedFilesToolViewModel>();
        services.AddSingleton<ViewModels.Docking.XviToolViewModel>();
        services.AddSingleton<ViewModels.Docking.OutlineToolViewModel>();
        services.AddSingleton<ViewModels.Docking.ProjectToolViewModel>();
        services.AddSingleton<ViewModels.Docking.LogToolViewModel>();       // #3
        services.AddSingleton<ViewModels.Docking.LivePreviewToolViewModel>();
        services.AddSingleton<ViewModels.Docking.MapViewerToolViewModel>();
        services.AddSingleton<ViewModels.Docking.Model3DViewerToolViewModel>();
        services.AddSingleton<ViewModels.Docking.StructuralGeologyToolViewModel>();
        services.AddSingleton<ViewModels.Docking.SettingsToolViewModel>();
        services.AddSingleton<Docking.DockFactory>();

        services.AddTransient<MainWindowViewModel>();

        // fail-fast DI validation. ValidateScopes catches captive/scoped-from-root misuse;
        // we then eagerly resolve the critical singletons so a missing/broken registration throws
        // at startup with a clear stack rather than NRE-ing deep in the UI later. (We deliberately
        // don't use ValidateOnBuild — it would also construct the transient MainWindowViewModel,
        // whose ctor has start-up side effects, an extra throwaway time.)
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        ValidateCriticalServices(_provider);
        return _provider;
    }

    /// <summary>
    /// eagerly resolves the services/tool view-models the shell can't run without, so a
    /// composition error surfaces immediately at startup. The DockFactory pulls in every dockable
    /// tool VM, and the document/workspace/build services cover the rest of the graph.
    /// </summary>
    private static void ValidateCriticalServices(IServiceProvider provider)
    {
        provider.GetRequiredService<ILogService>();
        provider.GetRequiredService<INotificationService>();
        provider.GetRequiredService<ICrashRecoveryService>();
        provider.GetRequiredService<IWorkspaceSymbolIndexStore>();
        provider.GetRequiredService<ITelemetryService>();
        provider.GetRequiredService<IWorkspaceSession>();
        provider.GetRequiredService<IDocumentService>();
        provider.GetRequiredService<ILayoutService>();
        provider.GetRequiredService<IAppSettingsService>();
        provider.GetRequiredService<IKeyboardShortcutService>();
        provider.GetRequiredService<ViewModels.BuildViewModel>();
        provider.GetRequiredService<Docking.DockFactory>();   // resolves all tool view-models
    }
}
