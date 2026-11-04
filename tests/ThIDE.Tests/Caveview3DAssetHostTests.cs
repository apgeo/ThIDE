// pure-logic tests for the 3D viewer asset host: MIME mapping, request→asset routing
// (incl. traversal rejection), free-port selection, and model staging. The HTTP listener itself
// and the avares read of viewer.html need the Avalonia platform, so they're exercised at runtime.

using System.IO;
using System.Linq;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

public class Caveview3DAssetHostTests
{
    [Theory]
    [InlineData("viewer.html", "text/html; charset=utf-8")]
    [InlineData("js/CaveView2.min.js", "text/javascript; charset=utf-8")]
    [InlineData("css/caveview.css", "text/css; charset=utf-8")]
    [InlineData("model/m1.lox", "application/octet-stream")]
    [InlineData("model/m1.3d", "application/octet-stream")]
    [InlineData("images/logo.svg", "image/svg+xml")]
    public void MimeType_is_correct(string path, string expected) =>
        Assert.Equal(expected, Caveview3DAssetHost.MimeTypeFor(path));

    [Theory]
    [InlineData("/", "Viewer", "viewer.html")]
    [InlineData("/viewer.html", "Viewer", "viewer.html")]
    [InlineData("/CaveView/js/CaveView2.min.js", "Runtime", "js/CaveView2.min.js")]
    [InlineData("/assets/station-mark.js", "Page", "assets/station-mark.js")]
    public void Request_path_maps_to_the_file_it_serves(string path, string kind, string expected)
    {
        var asset = Caveview3DAssetHost.MapRequestToAsset(path);
        Assert.NotNull(asset);
        Assert.Equal(kind, asset!.Value.Kind.ToString());
        Assert.Equal(expected, asset.Value.Path);
    }

    [Theory]
    [InlineData("/../secrets.txt")]
    [InlineData("/CaveView/../../etc")]
    [InlineData("/c:/windows")]
    [InlineData("/elsewhere.js")]
    public void Traversal_absolute_and_unknown_paths_are_rejected(string path) =>
        Assert.Null(Caveview3DAssetHost.MapRequestToAsset(path));

    [Fact]
    public void Every_file_viewer_html_loads_is_served()
    {
        // the host page names its scripts and stylesheet by path; each must map to a file that exists
        var html = File.ReadAllText(Path.Combine(RepoRoot(), "ThIDE", "Assets", "caveview", "viewer.html"));
        var refs = System.Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(refs);
        foreach (var r in refs)
        {
            var asset = Caveview3DAssetHost.MapRequestToAsset("/" + r);
            Assert.True(asset is not null, r);
            using var s = asset!.Value.Kind == Caveview3DAssetHost.AssetKind.Runtime
                ? Therion.CaveView.CaveViewRuntime.Open(asset.Value.Path)
                : Therion.CaveView.CaveViewRuntime.OpenPageFile(asset.Value.Path);
            Assert.True(s is not null, r);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ThIDE.sln not found above the test output");
    }

    [Fact]
    public void FindFreeLoopbackPort_returns_a_valid_port()
    {
        var port = Caveview3DAssetHost.FindFreeLoopbackPort();
        Assert.InRange(port, 1, 65535);
    }

    [Fact]
    public void StageModel_copies_supported_models_and_rejects_others()
    {
        var modelDir = Path.Combine(Path.GetTempPath(), "ThIDE-test", System.Guid.NewGuid().ToString("N"));
        using var host = new Caveview3DAssetHost(logger: null, modelDir: modelDir, assetExists: null, assetOpen: null);

        var lox = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".lox");
        File.WriteAllText(lox, "binary-ish");
        try
        {
            var name = host.StageModel(lox);
            Assert.NotNull(name);
            Assert.EndsWith(".lox", name);
            Assert.True(File.Exists(Path.Combine(modelDir, name!)));

            // Unsupported extension and a missing file both refuse to stage.
            var txt = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(txt, "nope");
            Assert.Null(host.StageModel(txt));
            Assert.Null(host.StageModel(Path.Combine(modelDir, "missing.3d")));
            File.Delete(txt);
        }
        finally { File.Delete(lox); }
    }

    [Fact]
    public void StageModel_replaces_the_previous_model()
    {
        var modelDir = Path.Combine(Path.GetTempPath(), "ThIDE-test", System.Guid.NewGuid().ToString("N"));
        using var host = new Caveview3DAssetHost(logger: null, modelDir: modelDir, assetExists: null, assetOpen: null);

        var a = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".lox");
        var b = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".3d");
        File.WriteAllText(a, "A");
        File.WriteAllText(b, "B");
        try
        {
            var first = host.StageModel(a);
            var second = host.StageModel(b);
            Assert.NotEqual(first, second);
            // Only the most-recently-staged model remains in the served dir.
            Assert.False(File.Exists(Path.Combine(modelDir, first!)));
            Assert.True(File.Exists(Path.Combine(modelDir, second!)));
        }
        finally { File.Delete(a); File.Delete(b); }
    }
}
