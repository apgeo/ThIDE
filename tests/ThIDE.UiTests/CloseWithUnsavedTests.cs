namespace ThIDE.UiTests;

/// <summary>Closing ThIDE with unsaved edits asks first: Cancel keeps it open, Save all writes and exits.</summary>
public class CloseWithUnsavedTests
{
    [UiFact]
    public async Task Exit_with_unsaved_edits_asks_before_closing()
    {
        var dir = Directory.CreateTempSubdirectory("thide-ui-close-").FullName;
        var path = Path.Combine(dir, "a.th");
        File.WriteAllText(path, "survey a\nendsurvey\n");

        await using var app = await ThideApp.StartAsync(dir, "a.th");
        await app.WaitAsync("role:TabItem;name:a.th;state:selected");
        await app.KeyAsync("type:TextArea", "Ctrl+End");
        await app.TypeAsync("# unsaved\n");

        await app.MenuAsync("File > Exit");
        await app.WaitAsync("#SaveBeforeBuild.Window");
        Assert.Contains("a.th", await app.TextOfAsync("#SaveBeforeBuild.Message"));
        await app.InvokeAsync("#SaveBeforeBuild.Cancel");
        await app.WaitAsync("#SaveBeforeBuild.Window", gone: true, timeoutMs: 10_000);
        Assert.False(app.HasExited);
        Assert.Contains("# unsaved", await app.TextOfAsync("type:TextEditor"));

        await app.MenuAsync("File > Exit");
        await app.WaitAsync("#SaveBeforeBuild.Window");
        try { await app.InvokeAsync("#SaveBeforeBuild.Save"); }
        catch (Exception) { /* the app may already be closing when the call returns */ }

        Assert.True(await app.WaitForExitAsync(TimeSpan.FromSeconds(30)), "the app did not exit after Save all");
        Assert.Equal("survey a\nendsurvey\n# unsaved\n", File.ReadAllText(path));
    }
}
