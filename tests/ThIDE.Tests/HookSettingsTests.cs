using System.Text.Json;
using Therion.Build.Automation;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class HookSettingsTests
{
    [Fact]
    public void Hook_list_survives_the_settings_file()
    {
        var settings = AppSettings.Default with
        {
            Hooks = [new HookDefinition(HookEvent.BuildFinished, "notify-send done", TimeoutSeconds: 5, Env: new Dictionary<string, string> { ["A"] = "1" })],
            EnableProjectAutomation = false,
        };

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;

        var hook = Assert.Single(back.Hooks);
        Assert.Equal(HookEvent.BuildFinished, hook.Event);
        Assert.Equal("notify-send done", hook.Command);
        Assert.Equal(5, hook.TimeoutSeconds);
        Assert.Equal("1", hook.Env!["A"]);
        Assert.False(back.EnableProjectAutomation);
    }

    [Fact]
    public void Preferences_rows_turn_back_into_hooks_and_skip_empty_ones()
    {
        var row = new HookRow(new HookDefinition(HookEvent.FileSaved, "lint {file}")) { TimeoutSeconds = 0, Detached = true };

        var hook = row.ToDefinition();

        Assert.Equal(1, hook.TimeoutSeconds);
        Assert.True(hook.Detached);
        Assert.Equal("lint {file}", hook.Command);
    }
}
