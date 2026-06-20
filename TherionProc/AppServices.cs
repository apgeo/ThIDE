// Implementation Plan §7.1 — DI composition root.
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
using Therion.Syntax;
using Therion.Workspace;
using TherionProc.Services;
using TherionProc.ViewModels;

namespace TherionProc;

internal static class AppServices
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("AppServices not initialized.");

    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // Logging (§13 M1 — Console + Debug providers; rolling file provider added in M5b).
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddSimpleConsole(o => o.SingleLine = true);
            b.AddDebug();
        });

        // Localization (§7.6).
        var resourcesPath = "Resources";
        services.AddLocalization(o => o.ResourcesPath = resourcesPath);
        services.AddSingleton<ILanguageService, LanguageService>();

        // Workspace primitives (§6).
        services.AddSingleton(WorkspaceOptions.FromEnvironment());
        services.AddSingleton<IThconfigSniffer, ThconfigSniffer>();
        services.AddSingleton<IProjectEntryPointResolver, ProjectEntryPointResolver>();

        // Parse caches (§4.5 / §M5 / Post-M6 D — disk cache is opt-in and format-selectable).
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

        // Semantics (§5 / §M6) — uses AddTherionSemantics() so rule plugins flow in via ISemanticRule.
        services.AddTherionSemantics();
        services.AddTherionBuiltinSemanticRules();

        // Syntax extensibility (§4.4) — command handlers register via ICommandHandler.
        services.AddTherionCommands();

        // Build / external tools (§9bis).
        services.AddSingleton<IExternalToolPathOverrides, JsonExternalToolPathOverrides>();
        services.AddSingleton<IExternalToolLocator>(sp =>
            new ExternalToolLocator(sp.GetRequiredService<IExternalToolPathOverrides>()));
        services.AddSingleton<ITherionOutputParser, HeuristicTherionOutputParser>();
        services.AddSingleton<IOutputArtifactCollector, OutputArtifactCollector>();
        services.AddSingleton<IOutputArtifactCache, JsonOutputArtifactCache>();
        services.AddSingleton<ICompileGate, CompileGate>();
        services.AddSingleton<IShellOpener, ShellOpener>();
        services.AddSingleton<ITherionCompiler, TherionCompiler>();

        // Active-document host (§7.3).
        services.AddSingleton<IDocumentService, DocumentService>();

        // Keyboard shortcuts (§9bis.5a / Decision #29).
        services.AddSingleton<IKeyboardShortcutService, JsonKeyboardShortcutService>();

        // Shell layout persistence (§7.2 / M6 #1 interim — Dock.Avalonia swap deferred).
        services.AddSingleton<ILayoutService, JsonLayoutService>();

        // ViewModels.
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ObjectBrowserViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<BuildViewModel>();
        services.AddTransient<WorkspaceExplorerViewModel>();
        services.AddTransient<XviReferencesViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<KeyboardShortcutsViewModel>();

        _provider = services.BuildServiceProvider();
        return _provider;
    }
}
