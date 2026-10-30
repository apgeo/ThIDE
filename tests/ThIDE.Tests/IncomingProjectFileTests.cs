using System.Text;
using System.Text.Json.Nodes;
using ThIDE.Services;

namespace ThIDE.Tests;

public class IncomingProjectFileTests
{
    private static string Toggle(string json, bool enabled)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Encoding.UTF8.GetString(IncomingFolderService.ReplaceEnabled(bytes, enabled) ?? IncomingFolderService.Rewritten(bytes, enabled));
    }

    [Fact]
    public void Turning_watching_off_keeps_comments_and_the_folder()
    {
        var json = "{\n  // shared with the club\n  \"Version\": 1,\n  \"Incoming\": { \"Folder\": \"drop\", \"Enabled\": true }\n}\n";

        Assert.Equal(json.Replace("\"Enabled\": true", "\"Enabled\": false"), Toggle(json, false));
    }

    [Fact]
    public void A_file_without_the_setting_gains_it_once_in_the_existing_casing()
    {
        var result = JsonNode.Parse(Toggle("{ \"Incoming\": { \"Folder\": \"drop\" } }", true))!.AsObject();

        Assert.Single(result);
        Assert.Equal("drop", (string?)result["Incoming"]!["Folder"]);
        Assert.True((bool)result["Incoming"]!["enabled"]!);
    }

    [Fact]
    public void An_enabled_flag_outside_incoming_is_left_alone()
    {
        var json = "{ \"other\": { \"enabled\": true }, \"incoming\": { \"enabled\": true } }";

        Assert.Equal("{ \"other\": { \"enabled\": true }, \"incoming\": { \"enabled\": false } }", Toggle(json, false));
    }
}
