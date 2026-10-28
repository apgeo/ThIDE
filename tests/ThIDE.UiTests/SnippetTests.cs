namespace ThIDE.UiTests;

/// <summary>Tab-expanded snippets with fields, in the real editor.</summary>
public class SnippetTests
{
    [UiFact]
    public async Task Tab_expands_a_snippet_and_typing_fills_its_first_field()
    {
        var dir = ThideApp.CopyFixture("demo-cave");
        await using var app = await ThideApp.StartAsync(dir, "demo.thconfig");
        await app.WaitAsync("role:TabItem;name:demo.thconfig;state:selected");

        await app.KeyAsync("type:TextArea", "Ctrl+End");
        await app.TypeAsync("\ninp");
        await app.KeyAsync("type:TextArea", "Tab");
        Assert.EndsWith("input file.th", (await app.TextOfAsync("type:TextEditor")).TrimEnd());

        await app.TypeAsync("extra.th");
        Assert.EndsWith("input extra.th", (await app.TextOfAsync("type:TextEditor")).TrimEnd());
    }
}
