// Go to File (Ctrl+P) and the active thconfig: the files it compiles come first, the others are
// greyed and say so; with "only files connected to the active thconfig" they are left out, a
// recently opened one included.

using Avalonia.Media;
using Therion.Core;
using Therion.Semantics;
using Therion.Syntax;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels.QuickPick;

namespace ThIDE.Tests;

public sealed class QuickOpenProviderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-quickopen-").FullName;
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private sealed class NoIcons : IFileIconProvider
    {
        public IImage? GetIcon(string path, bool isDirectory) => null;
    }

    private string Write(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "encoding utf-8\n");
        return path;
    }

    /// <summary>A project whose thconfig compiles data/cave.th; old/cave.th is an archived copy.</summary>
    private (QuickOpenProvider Provider, string Compiled, string Archived) Project(QuickOpenSources sources)
    {
        var cfg = Write("cave.thconfig");
        var compiled = Write(Path.Combine("data", "cave.th"));
        var archived = Write(Path.Combine("old", "cave.th"));
        var covered = new HashSet<string>(StringComparer.Ordinal) { cfg, compiled };
        string? asked = null;
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name switch
        {
            "get_RootPath" => _root,
            "get_ActiveThconfig" => new ThconfigCandidate(cfg, "cave.thconfig", false),
            "get_Model" => WorkspaceSemanticModel.Build(new Dictionary<string, ParseResult<TherionFile>>(), Array.Empty<XviFile>()),
            "Covers" => covered.Contains(asked!),
            _ => null,
        }, (m, args) => { if (m.Name == "Covers") asked = (string)args[0]!; });
        // The archived copy was opened recently, so history lists it before the folder walk does.
        var settings = new FakeSettings(AppSettings.Default with { QuickOpenSources = sources, RecentFiles = [archived] });
        return (new QuickOpenProvider(new NullDocumentService(), session, settings, new NoIcons()), compiled, archived);
    }

    private static List<QuickPickItem> Search(QuickOpenProvider provider, string text)
    {
        var palette = provider.CreatePalette(_ => Task.CompletedTask);
        palette.SearchText = text;
        return palette.Results.Where(r => r.Title == "cave.th").ToList();
    }

    [Theory]
    [InlineData("cave")]
    [InlineData("")]
    public void Files_of_the_active_thconfig_come_first_and_the_others_are_marked(string text)
    {
        var (provider, compiled, archived) = Project(QuickOpenSources.History | QuickOpenSources.Directory);

        var hits = Search(provider, text);

        Assert.Equal([compiled, archived], hits.Select(h => (string)h.Payload!));
        Assert.False(hits[0].IsDimmed);
        Assert.True(hits[1].IsDimmed);
        Assert.EndsWith(string.Format(Tr.Get("QuickOpen_NotInConfig"), "cave.thconfig"), hits[1].Detail);
    }

    [Fact]
    public void Connected_only_leaves_out_a_recent_file_the_thconfig_does_not_compile()
    {
        var (provider, compiled, _) = Project(QuickOpenSources.History | QuickOpenSources.ThconfigConnected);

        var hits = Search(provider, "cave");

        Assert.Equal([compiled], hits.Select(h => (string)h.Payload!));
    }

    [Fact]
    public async Task A_loaded_workspace_marks_the_copy_its_thconfig_does_not_source()
    {
        File.WriteAllText(Path.Combine(_root, "cave.thconfig"), "encoding utf-8\nsource cave.th\n");
        File.WriteAllText(Path.Combine(_root, "cave.th"), "survey cave\nendsurvey\n");
        var archived = Write(Path.Combine("old", "cave.th"));
        await using var session = new WorkspaceSessionService(new StubSniffer(), new FakeSettings());
        await session.SetRootAsync(_root);
        var provider = new QuickOpenProvider(new NullDocumentService(), session, new FakeSettings(), new NoIcons());

        var hits = Search(provider, "cave");

        Assert.Equal(archived, (string)hits[^1].Payload!);
        Assert.True(hits[^1].IsDimmed);
        Assert.False(hits[0].IsDimmed);
    }
}
