// The opt-in "rename in comments too" pass must locate its hits in the text decoded the way Therion
// reads the file (its `encoding` line), because the hits are applied to, and written over, that text.

using System.Text;
using Therion.Syntax;
using Therion.Workspace;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.Views;

namespace ThIDE.Tests;

public class CommentRenameEncodingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-comment-rename-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private const string Header =
        "encoding windows-1250\n" +
        "survey s\n" +
        "  centreline\n" +
        "    data normal from to length compass clino\n" +
        "    st1 st2 1 0 0\n" +
        "  endcentreline\n" +
        "endsurvey\n";

    private async Task<(string Path, string Text, Therion.Semantics.WorkspaceSemanticModel Model)> LoadAsync(string commentLine)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Header + commentLine + "\n";
        var path = Path.Combine(_root, "cave.th");
        File.WriteAllBytes(path, Encoding.GetEncoding(1250).GetBytes(text));
        var cfg = Path.Combine(_root, "thconfig");
        File.WriteAllText(cfg, "source cave.th\n");

        // Precondition: the resolver honours the directive, so the parser-side passes see the real text.
        Assert.Equal(text, EncodingResolver.ReadAllText(path));

        await using var ws = new TherionWorkspace();
        await ws.LoadAsync(cfg);
        var model = ws.BuildSemanticModel();
        Assert.Contains(path, model.PerFile.Keys);
        return (path, text, model);
    }

    private static List<RenameFileChanges> CollectCommentChanges(
        Therion.Semantics.WorkspaceSemanticModel model, string oldName, List<RenameFileChanges> symbolChanges) =>
        RenamePlanner.CollectCommentRenameChanges(model, null, oldName, symbolChanges);

    private static List<RenameFileChanges> Merge(List<RenameFileChanges> a, List<RenameFileChanges> b) =>
        RenamePlanner.MergeRenameChanges(a, b);

    /// <summary>What the apply step does with a file's planned hits.</summary>
    private static string Apply(RenameFileChanges fc, string newName)
    {
        var sb = new StringBuilder(fc.FileText);
        foreach (var (start, length) in fc.Hits.OrderByDescending(h => h.Start))
            sb.Remove(start, length).Insert(start, newName);
        return sb.ToString();
    }

    [Fact]
    public async Task Comment_only_hits_are_saved_back_in_the_files_encoding()
    {
        // cp1250 "ă" is the single byte E3, which is not valid UTF-8 on its own.
        var (path, text, model) = await LoadAsync("# ă st1 named in a comment");

        var change = Assert.Single(CollectCommentChanges(model, "st1", []));
        var renamed = Apply(change, "st9");

        // The symbol pass found nothing (say, the station is declared elsewhere); the comment pass alone
        // must still produce text the file's own encoding can store, and store it unchanged.
        byte[]? bytes = null;
        var failure = Record.Exception(() => bytes = SourceFileIo.EncodeForSave(path, renamed, null).Bytes);
        Assert.Null(failure);
        Assert.Equal(Encoding.GetEncoding(1250).GetBytes(text.Replace("st1 named", "st9 named")), bytes);
    }

    [Fact]
    public async Task Comment_hits_merged_with_symbol_hits_replace_the_right_characters()
    {
        // cp1250 "Ă”" is the byte pair C3 94, which UTF-8 reads as the single character "Ô": every
        // offset after it is one short when the file is decoded as UTF-8 instead of per its directive.
        var (path, text, model) = await LoadAsync("# Ă” st1 named in a comment");

        // The symbol pass (decoded per the encoding line) renames the data-line occurrence.
        int dataHit = text.IndexOf("st1 st2", StringComparison.Ordinal);
        var symbolChanges = new List<RenameFileChanges> { new(path, text, [(dataHit, 3)]) };

        var merged = Assert.Single(Merge(symbolChanges, CollectCommentChanges(model, "st1", symbolChanges)));
        var renamed = Apply(merged, "st9");

        Assert.Equal(text.Replace("st1", "st9"), renamed);
    }
}
