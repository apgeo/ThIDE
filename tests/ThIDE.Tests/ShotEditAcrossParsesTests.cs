// The Object Browser's shot rows are lowered from the workspace session's parse, while an inline
// edit is applied to the active document's own AST (parsed by the editor). Both come from the same
// text, so a row taken from one must be replaceable in the other.

using Therion.Semantics;
using Therion.Workspace;
using ThIDE.Services;

namespace ThIDE.Tests;

public class ShotEditAcrossParsesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-shotedit-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    [Fact]
    public async Task Shot_row_from_the_workspace_model_is_found_in_the_editor_ast_of_the_same_text()
    {
        const string text =
            "survey cave\n" +
            "  centreline\n" +
            "    data normal from to length compass clino\n" +
            "    1 2 10.0 90 0\n" +
            "  endcentreline\n" +
            "endsurvey\n";
        var path = Path.Combine(_root, "cave.th");
        File.WriteAllText(path, text);

        // The grid rows carry the session's DataRow nodes ...
        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(path);
        var model = ws.BuildSemanticModel();
        var shot = model.PerFile[path].Shots.Single(s => s.SourceRow is not null);
        var row = shot.SourceRow!;

        // ... while the edit goes against the AST the editor parsed for the active document.
        var editorAst = DocumentParser.Parse(path, text).Ast!;

        var replacement = row with { Values = row.Values.SetItem(2, "12.5") };
        var result = new ModelEditService().ReplaceNode(editorAst, row, replacement);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.ToString())));
        Assert.Contains("12.5", result.UpdatedText);
    }
}
