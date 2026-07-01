// pure-logic tests for the 3D viewer asset host: MIME mapping, request→asset routing
// (incl. traversal rejection), free-port selection, and model staging. The HTTP listener itself
// and the avares asset reads need the Avalonia platform, so they're exercised at runtime.

using System.IO;
using TherionProc.Services;
using Xunit;

namespace TherionProc.Tests;

public class Caveview3DAssetHostTests
{
    [Theory]
    [InlineData("viewer.html", "text/html; charset=utf-8")]
    [InlineData("CaveView/js/CaveView2.js", "text/javascript; charset=utf-8")]
    [InlineData("CaveView/css/caveview.css", "text/css; charset=utf-8")]
    [InlineData("model/m1.lox", "application/octet-stream")]
    [InlineData("model/m1.3d", "application/octet-stream")]
    [InlineData("CaveView/images/logo.svg", "image/svg+xml")]
    public void MimeType_is_correct(string path, string expected) =>
        Assert.Equal(expected, Caveview3DAssetHost.MimeTypeFor(path));

    [Theory]
    [InlineData("/", "avares://TherionProc/Assets/caveview/viewer.html")]
    [InlineData("/viewer.html", "avares://TherionProc/Assets/caveview/viewer.html")]
    [InlineData("/CaveView/js/CaveView2.js", "avares://TherionProc/Assets/caveview/CaveView/js/CaveView2.js")]
    public void Request_path_maps_to_avares_asset(string path, string expected) =>
        Assert.Equal(expected, Caveview3DAssetHost.MapRequestToAsset(path));

    [Theory]
    [InlineData("/../secrets.txt")]
    [InlineData("/CaveView/../../etc")]
    [InlineData("/c:/windows")]
    public void Traversal_and_absolute_paths_are_rejected(string path) =>
        Assert.Null(Caveview3DAssetHost.MapRequestToAsset(path));

    [Fact]
    public void FindFreeLoopbackPort_returns_a_valid_port()
    {
        var port = Caveview3DAssetHost.FindFreeLoopbackPort();
        Assert.InRange(port, 1, 65535);
    }

    [Fact]
    public void StageModel_copies_supported_models_and_rejects_others()
    {
        var modelDir = Path.Combine(Path.GetTempPath(), "TherionProc-test", System.Guid.NewGuid().ToString("N"));
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
        var modelDir = Path.Combine(Path.GetTempPath(), "TherionProc-test", System.Guid.NewGuid().ToString("N"));
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
