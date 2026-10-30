using System.Text;

namespace ThIDE.UiTests;

/// <summary>A file in a legacy encoding survives being opened, edited and saved.</summary>
public class EncodingTests
{
    [UiFact]
    public async Task A_latin1_file_is_saved_back_in_latin1()
    {
        var dir = Directory.CreateTempSubdirectory("thide-ui-enc-").FullName;
        var path = Path.Combine(dir, "legacy.th");
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes("encoding iso8859-1\nsurvey grotte -title \"Grotte de la Fée\"\nendsurvey\n"));

        await using var app = await ThideApp.StartAsync(dir, "legacy.th");
        await app.WaitAsync("role:TabItem;name:legacy.th;state:selected");
        Assert.Contains("Fée", await app.TextOfAsync("type:TextEditor"));

        await app.KeyAsync("type:TextArea", "Ctrl+End");
        await app.TypeAsync("# vérifié\n");
        Assert.Contains("vérifié", await app.TextOfAsync("type:TextEditor"));
        await app.InvokeAsync("#Toolbar.Save");

        for (int i = 0; i < 50 && !Encoding.Latin1.GetString(File.ReadAllBytes(path)).Contains("vérifié"); i++)
            await Task.Delay(100);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("encoding iso8859-1\nsurvey grotte -title \"Grotte de la Fée\"\nendsurvey\n# vérifié\n", Encoding.Latin1.GetString(bytes));
        Assert.DoesNotContain((byte)0xC3, bytes);   // no UTF-8 sequences crept in
    }
}
