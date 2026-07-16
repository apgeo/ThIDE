// T-03.5: the get_setting/set_setting whitelist. Pure (AppSettings in, AppSettings out), so it tests
// directly with no UI: the whitelist boundary, per-type validation, and the privilege exclusions
// (the agent must not be able to grant itself UI control or flip security-weight settings).

using System.Linq;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public class McpSettingKeysTests
{
    private static readonly AppSettings Defaults = AppSettings.Default;

    [Fact]
    public void List_exposes_the_whitelist_with_current_values()
    {
        var list = McpSettingKeys.List(Defaults);

        Assert.Contains(list, s => s.Key == "editor.wordWrap" && s.Value == "false" && s.Type == "bool");
        Assert.Contains(list, s => s.Key == "editor.fontSize" && s.Value == "13" && s.Type == "number");
        Assert.Contains(list, s => s.Key == "theme.mode" && s.Type == "enum" && s.Options.Contains("Dark"));
    }

    [Fact]
    public void Get_reads_a_known_key_and_ignores_an_unknown_one()
    {
        Assert.NotNull(McpSettingKeys.Get(Defaults, "editor.wordWrap"));
        Assert.NotNull(McpSettingKeys.Get(Defaults, "EDITOR.WORDWRAP"));   // case-insensitive
        Assert.Null(McpSettingKeys.Get(Defaults, "editor.fontFamily"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    public void A_bool_round_trips(string value, bool expected)
    {
        var (ok, next, error) = McpSettingKeys.TrySet(Defaults, "editor.wordWrap", value);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, next.EditorWordWrap);
    }

    [Fact]
    public void A_bad_bool_is_refused_and_leaves_the_settings_unchanged()
    {
        var (ok, next, error) = McpSettingKeys.TrySet(Defaults, "editor.wordWrap", "yes");

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Same(Defaults, next);
    }

    [Theory]
    [InlineData("dark", "Dark")]
    [InlineData("LIGHT", "Light")]
    [InlineData("System", "System")]
    public void An_enum_matches_case_insensitively_and_stores_the_canonical_form(string value, string expected)
    {
        var (ok, next, _) = McpSettingKeys.TrySet(Defaults, "theme.mode", value);

        Assert.True(ok);
        Assert.Equal(expected, next.ThemeMode);
    }

    [Fact]
    public void An_unlisted_enum_value_is_refused()
    {
        var (ok, _, error) = McpSettingKeys.TrySet(Defaults, "theme.mode", "Solarized");
        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("14", true)]
    [InlineData("6", true)]
    [InlineData("48", true)]
    [InlineData("100", false)]   // above the ceiling
    [InlineData("3", false)]     // below the floor
    [InlineData("big", false)]   // not a number
    public void A_number_is_range_checked(string value, bool expectedOk)
    {
        var (ok, next, _) = McpSettingKeys.TrySet(Defaults, "editor.fontSize", value);

        Assert.Equal(expectedOk, ok);
        if (expectedOk) Assert.Equal(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), next.EditorFontSize);
    }

    [Fact]
    public void A_set_is_visible_to_a_later_get()
    {
        var (_, next, _) = McpSettingKeys.TrySet(Defaults, "ui.language", "ro");
        Assert.Equal("ro", McpSettingKeys.Get(next, "ui.language")!.Value);
    }

    [Theory]
    [InlineData("mcp.followAgent")]     // privilege: the agent must not grant itself UI control
    [InlineData("EnableMcpServer")]
    [InlineData("telemetry.enabled")]
    [InlineData("hook.onSave")]
    public void Security_and_privilege_settings_are_not_on_the_whitelist(string key)
    {
        Assert.Null(McpSettingKeys.Get(Defaults, key));
        var (ok, _, _) = McpSettingKeys.TrySet(Defaults, key, "true");
        Assert.False(ok);
    }
}
