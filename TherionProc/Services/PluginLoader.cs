// EXT-04 — plugin / extension surface.
//
// A minimal but real extension point: external assemblies dropped into the plugins folder are
// scanned for public types implementing Therion.Semantics.ISemanticRule (the existing rule
// plugin contract). Discovered rules are instantiated and registered so the rule runner picks
// them up alongside the built-ins — adding custom diagnostics without recompiling the app.
//
// Gated by AppSettings.EnablePlugins (default on) since plugin rules run during analysis and add
// processing time. Loading is best-effort: a bad plugin is logged and skipped, never fatal.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Therion.Semantics;

namespace TherionProc.Services;

public static class PluginLoader
{
    /// <summary>The folder scanned for plugin assemblies (<c>%AppData%/TherionProc/plugins</c>).</summary>
    public static string DefaultPluginDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(appData, "TherionProc", "plugins");
    }

    /// <summary>
    /// Loads every <see cref="ISemanticRule"/> implementation found in the *.dll files under
    /// <paramref name="directory"/>. Returns an empty list when the folder is absent. <paramref
    /// name="log"/> receives one message per loaded rule or failed assembly.
    /// </summary>
    public static IReadOnlyList<ISemanticRule> LoadSemanticRules(string directory, Action<string>? log = null)
    {
        var rules = new List<ISemanticRule>();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return rules;

        foreach (var dll in Directory.EnumerateFiles(directory, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                foreach (var type in SafeGetTypes(assembly))
                {
                    if (type is null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(ISemanticRule).IsAssignableFrom(type)) continue;
                    if (type.GetConstructor(Type.EmptyTypes) is null) continue;
                    if (Activator.CreateInstance(type) is ISemanticRule rule)
                    {
                        rules.Add(rule);
                        log?.Invoke($"Loaded plugin rule '{rule.Id}' from {Path.GetFileName(dll)}.");
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Plugin '{Path.GetFileName(dll)}' could not be loaded: {ex.Message}");
            }
        }
        return rules;
    }

    // A plugin compiled against a different dependency version may throw on GetTypes(); take the
    // types it could load rather than failing the whole assembly.
    private static IEnumerable<Type?> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types; }
    }
}
