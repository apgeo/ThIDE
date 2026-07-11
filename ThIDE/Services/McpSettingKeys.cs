// The whitelist of application settings the MCP ring-R3 get_setting/set_setting tools may touch
// (T-03.5). Deliberately small and safe: editor/diagnostics/build/theme/language preferences a caver
// might reasonably ask the assistant to flip. It never exposes anything with security or privilege
// weight — EnableMcpServer, McpFollowAgent (the agent must not grant itself UI control), telemetry,
// hooks, plugins are all absent. Pure: AppSettings in, a modified AppSettings (or an error) out — no
// UI, no dispatcher — so it is unit-tested directly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Therion.Mcp;

namespace ThIDE.Services;

public static class McpSettingKeys
{
    private sealed record Spec(
        string Key, string Type, string Description, IReadOnlyList<string> Options,
        Func<AppSettings, string> Get,
        Func<AppSettings, string, (bool Ok, AppSettings Next, string? Error)> Set);

    private static readonly string[] BoolOptions = ["true", "false"];
    private static readonly string[] ThemeOptions = ["System", "Light", "Dark"];
    private static readonly string[] LanguageOptions = ["en", "ro"];

    private static readonly Spec[] Specs =
    [
        Bool("editor.wordWrap", "Wrap long lines in the editor.",
            s => s.EditorWordWrap, (s, b) => s with { EditorWordWrap = b }),
        Bool("editor.formatOnSave", "Run Format Document automatically on save.",
            s => s.EditorFormatOnSave, (s, b) => s with { EditorFormatOnSave = b }),
        Bool("editor.showLineNumbers", "Show line numbers in the editor gutter.",
            s => s.ShowLineNumbers, (s, b) => s with { ShowLineNumbers = b }),
        Bool("editor.showWhitespace", "Render spaces and tabs in the editor.",
            s => s.EditorShowWhitespace, (s, b) => s with { EditorShowWhitespace = b }),
        Number("editor.fontSize", "Editor font size in points (6–48).", 6, 48,
            s => s.EditorFontSize, (s, d) => s with { EditorFontSize = d }),
        Bool("build.compileOnSave", "Rebuild the active project a moment after each save.",
            s => s.CompileOnSave, (s, b) => s with { CompileOnSave = b }),
        Bool("diagnostics.workspaceScope",
            "Show project-wide diagnostics (all files + cross-file analysis) rather than only the active file's.",
            s => s.DiagnosticsWorkspaceScope, (s, b) => s with { DiagnosticsWorkspaceScope = b }),
        Bool("diagnostics.localFixGrounds",
            "Treat a bare local 'fix' (no cs) as grounding a disconnected survey piece (suppresses TH_SEM_015).",
            s => s.LocalFixGroundsDisconnected, (s, b) => s with { LocalFixGroundsDisconnected = b }),
        Enum("theme.mode", "Application theme.", ThemeOptions,
            s => s.ThemeMode, (s, v) => s with { ThemeMode = v }),
        Enum("ui.language", "UI language (applied at startup).", LanguageOptions,
            s => s.UiLanguage, (s, v) => s with { UiLanguage = v }),
    ];

    /// <summary>All whitelisted settings with their current values.</summary>
    public static IReadOnlyList<McpSettingInfo> List(AppSettings settings) =>
        Specs.Select(sp => Info(sp, settings)).ToList();

    /// <summary>One whitelisted setting by key (case-insensitive), or null when the key is not whitelisted.</summary>
    public static McpSettingInfo? Get(AppSettings settings, string key) =>
        Find(key) is { } sp ? Info(sp, settings) : null;

    /// <summary>Produces a copy of <paramref name="settings"/> with <paramref name="key"/> set, or an error.</summary>
    public static (bool Ok, AppSettings Next, string? Error) TrySet(AppSettings settings, string key, string value) =>
        Find(key) is { } sp ? sp.Set(settings, value) : (false, settings, $"'{key}' is not a settable setting.");

    private static Spec? Find(string key) =>
        Specs.FirstOrDefault(sp => string.Equals(sp.Key, key, StringComparison.OrdinalIgnoreCase));

    private static McpSettingInfo Info(Spec sp, AppSettings s) =>
        new(sp.Key, sp.Get(s), sp.Type, sp.Description, sp.Options);

    // ---- spec factories --------------------------------------------------------------------------

    private static Spec Bool(string key, string description,
        Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set) =>
        new(key, "bool", description, BoolOptions,
            s => get(s) ? "true" : "false",
            (s, v) => bool.TryParse((v ?? string.Empty).Trim(), out var b)
                ? (true, set(s, b), null)
                : (false, s, $"Expected true or false, got '{v}'."));

    private static Spec Number(string key, string description, double min, double max,
        Func<AppSettings, double> get, Func<AppSettings, double, AppSettings> set) =>
        new(key, "number", description, Array.Empty<string>(),
            s => get(s).ToString(CultureInfo.InvariantCulture),
            (s, v) => double.TryParse((v ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                      && d >= min && d <= max
                ? (true, set(s, d), null)
                : (false, s, $"Expected a number between {min} and {max}, got '{v}'."));

    private static Spec Enum(string key, string description, IReadOnlyList<string> options,
        Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set) =>
        new(key, "enum", description, options,
            get,
            (s, v) =>
            {
                var match = options.FirstOrDefault(o => string.Equals(o, (v ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                return match is null
                    ? (false, s, $"Expected one of {string.Join(", ", options)}, got '{v}'.")
                    : (true, set(s, match), null);
            });
}
