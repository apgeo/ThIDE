// "Show in 3D" — pure logic of the request sent to the viewer page and of what it reports back:
//   * Model3DTargetFor: a shot's named end is a station; a splay's anonymous end is the splay of
//     the named end with the shot's readings, turned round for a splay read towards the station.
//   * ShowScript: the cvShow(name, opts) call, with the close-up switch and the splay readings.
//   * ShownStatus: a status line for each kind of answer, nothing for a superseded flight.
//   * ViewerLanguageQuery: only languages the viewer has a catalogue for.

using System.Globalization;
using System.Text.Json;
using ThIDE.Services;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class Model3DViewerShowTests
{
    private static MeasurementRow Shot(string from, string to, double? len = 2.5, double? az = 30, double? cl = -10) => new()
    {
        FromFull = "cave." + from, FromShort = from, ToFull = "cave." + to, ToShort = to,
        Length = len, Compass = az, Clino = cl,
    };

    [Fact]
    public void Named_ends_of_a_leg_are_stations()
    {
        var row = Shot("1", "2");
        Assert.Equal(new Model3DTarget("cave.1"), MeasurementsViewModel.Model3DTargetFor(row, toEnd: false));
        Assert.Equal(new Model3DTarget("cave.2"), MeasurementsViewModel.Model3DTargetFor(row, toEnd: true));
    }

    [Theory]
    [InlineData("-")]
    [InlineData(".")]
    public void The_anonymous_end_of_a_splay_is_the_splay_of_its_station(string anon)
    {
        var t = MeasurementsViewModel.Model3DTargetFor(Shot("1", anon), toEnd: true);
        Assert.True(t.IsSplay);
        Assert.Equal("cave.1", t.Name);
        Assert.Equal((2.5, 30.0, -10.0), (t.SplayLength!.Value, t.SplayCompass!.Value, t.SplayClino!.Value));

        // the station end of the same splay is the station itself
        Assert.False(MeasurementsViewModel.Model3DTargetFor(Shot("1", anon), toEnd: false).IsSplay);
    }

    [Fact]
    public void A_splay_read_towards_its_station_is_turned_round()
    {
        var t = MeasurementsViewModel.Model3DTargetFor(Shot("-", "7", az: 350, cl: 20), toEnd: false);
        Assert.Equal("cave.7", t.Name);
        Assert.Equal(170, t.SplayCompass!.Value, 6);
        Assert.Equal(-20, t.SplayClino!.Value, 6);
    }

    [Fact]
    public void ShowScript_passes_the_name_the_switch_and_the_splay()
    {
        var plain = Model3DViewerViewModel.ShowScript(new Model3DTarget("cave.\"x\".1"), zoom: true);
        Assert.StartsWith("cvShow(\"cave.\\u0022x\\u0022.1\",", plain);
        var opts = JsonDocument.Parse(plain[(plain.IndexOf(',') + 1)..^1]).RootElement;
        Assert.True(opts.GetProperty("zoom").GetBoolean());
        Assert.Equal(JsonValueKind.Null, opts.GetProperty("splay").ValueKind);

        var splay = Model3DViewerViewModel.ShowScript(Model3DTarget.Splay("cave.1", 2, 90, 5), zoom: false);
        var o2 = JsonDocument.Parse(splay[(splay.IndexOf(',') + 1)..^1]).RootElement;
        Assert.False(o2.GetProperty("zoom").GetBoolean());
        Assert.Equal(90, o2.GetProperty("splay").GetProperty("compass").GetDouble());
        Assert.Equal(5, o2.GetProperty("splay").GetProperty("clino").GetDouble());
    }

    [Fact]
    public void ShownStatus_says_what_was_found()
    {
        Assert.Contains("cave.1", Model3DViewerViewModel.ShownStatus("cave.1", "station", null, null, null));
        // a source name the page resolved to the model's own longer path names the model's path
        Assert.Contains("top.cave.1", Model3DViewerViewModel.ShownStatus("cave.1", "station", "top.cave.1", null, null));
        Assert.Contains("12", Model3DViewerViewModel.ShownStatus("cave.1", "station", null, "closest", 12));
        Assert.NotEqual(
            Model3DViewerViewModel.ShownStatus("cave.1", "station", null, "missing", null),
            Model3DViewerViewModel.ShownStatus("cave.1", "station", null, null, null));
        Assert.Contains("cave", Model3DViewerViewModel.ShownStatus("cave", "survey", null, null, null));
        Assert.Contains("nope", Model3DViewerViewModel.ShownStatus("nope", "none", null, null, null));
        Assert.Equal(string.Empty, Model3DViewerViewModel.ShownStatus("cave.1", "cancelled", null, null, null));
    }

    [Theory]
    [InlineData("ro-RO", "?lang=ro")]
    [InlineData("en-US", "")]
    [InlineData("de-DE", "")]
    public void ViewerLanguageQuery_names_only_languages_with_a_catalogue(string culture, string expected) =>
        Assert.Equal(expected, Model3DViewerViewModel.ViewerLanguageQuery(new CultureInfo(culture)));
}

public class Model3DViewerShowSequencingTests
{
    private sealed class FakeHost : ICaveview3DAssetHost
    {
        public bool IsAvailable => true;
        public bool IsListening => true;
        public bool TryStart() => true;
        public string? ViewerUrl => "http://127.0.0.1:1/viewer.html";
        public string? StageModel(string modelPath) => "m1.lox";
        public void Dispose() { }
    }

    private sealed class FakeShell : Therion.Build.IShellOpener
    {
        public bool Open(string path) => true;
        public bool RevealInFileManager(string path) => true;
    }

    [Fact]
    public void Show_waits_for_the_model_and_its_controls_before_flying()
    {
        var model = System.IO.Path.GetTempFileName() + ".lox";
        System.IO.File.WriteAllText(model, "x");
        try
        {
            var vm = new Model3DViewerViewModel(new FakeHost(), new StationSourceResolver(), new NullDocumentService(), new FakeShell());
            var scripts = new System.Collections.Generic.List<string>();
            vm.ScriptRequested += (_, js) => scripts.Add(js);

            vm.LoadModel(model);
            vm.SelectInModel(new Model3DTarget("demo.3"));
            vm.OnWebMessage("{\"type\":\"ready\"}");
            Assert.Contains(scripts, s => s.StartsWith("cvLoadModel(", System.StringComparison.Ordinal));
            Assert.DoesNotContain(scripts, s => s.StartsWith("cvShow(", System.StringComparison.Ordinal));

            vm.OnWebMessage("{\"type\":\"loaded\"}");
            Assert.StartsWith("cvShow(\"demo.3\"", scripts[^1]);

            // once loaded, a show goes straight out; a splay switches the Splays layer on
            scripts.Clear();
            Assert.False(vm.SplaysOn);
            vm.SelectInModel(Model3DTarget.Splay("demo.3", 2, 10, 0));
            Assert.True(vm.SplaysOn);
            Assert.StartsWith("cvShow(\"demo.3\"", scripts[^1]);

            // the page's answer becomes the status line
            vm.OnWebMessage("{\"type\":\"shown\",\"target\":\"demo.9\",\"kind\":\"none\"}");
            Assert.Contains("demo.9", vm.Status);
        }
        finally { System.IO.File.Delete(model); }
    }
}
