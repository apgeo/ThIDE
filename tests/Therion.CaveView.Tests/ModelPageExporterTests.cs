using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Therion.CaveView;

namespace Therion.CaveView.Tests;

public class CaveViewRuntimeTests
{
    [Fact]
    public void Runtime_carries_the_bundle_workers_stylesheet_and_catalogues()
    {
        Assert.True(CaveViewRuntime.IsAvailable);
        var files = CaveViewRuntime.Files;
        Assert.Contains("js/CaveView2.min.js", files);
        Assert.Contains("js/workers/plyLoaderWorker.js", files);
        Assert.Contains("css/caveview.css", files);
        Assert.Contains("lib/Detector.js", files);
        Assert.Contains("LICENSE", files);
        foreach (var lang in CaveViewRuntime.Languages.Where(l => l != "en"))
            Assert.Contains($"lib/lang-{lang}.json", files);
        Assert.All(files, f => Assert.DoesNotContain('\\', f));
    }

    [Fact]
    public void Bundle_is_the_version_the_runtime_names()
    {
        // the fork stamps its distribution version into the bundle; a re-vendored bundle that
        // forgot to bump Version (or the reverse) fails here
        using var s = CaveViewRuntime.Open("js/CaveView2.min.js")!;
        using var r = new StreamReader(s);
        Assert.Contains("\"" + CaveViewRuntime.Version + "\"", r.ReadToEnd());
    }

    [Fact]
    public void Bundle_carries_the_api_the_viewer_and_page_call()
    {
        using var s = CaveViewRuntime.Open("js/CaveView2.min.js")!;
        var js = new StreamReader(s).ReadToEnd();
        foreach (var api in new[] { "getStationScreenPosition", "getStationSplays", "focusStation", "focusSurvey", "CaveViewToolbar" })
            Assert.Contains(api, js);
    }

    [Fact]
    public void Open_returns_null_for_a_file_it_does_not_have_and_accepts_either_separator()
    {
        Assert.Null(CaveViewRuntime.Open("js/nope.js"));
        Assert.Null(CaveViewRuntime.Open(""));
        using var s = CaveViewRuntime.Open("js\\CaveView2.min.js");
        Assert.NotNull(s);
        using var p = CaveViewRuntime.OpenPageFile("/assets/station-mark.js");
        Assert.NotNull(p);
    }
}

public class ModelPageExporterTests
{
    private static readonly byte[] FakeModel = Encoding.ASCII.GetBytes("not really a lox file");

    private static ModelPageModel Model(string name = "Peștera Mare.lox") => new(name, () => new MemoryStream(FakeModel));

    private static ModelPageOptions Options(Func<ModelPageOptions, ModelPageOptions>? edit = null)
    {
        var o = new ModelPageOptions { Title = "Test cave", Description = "Line one\nLine <two>", Generator = "ThIDE test" };
        return edit is null ? o : edit(o);
    }

    private static string Config(string html)
    {
        var m = Regex.Match(html, "<script type=\"application/json\" id=\"cave-page-config\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(m.Success, "config block missing");
        return m.Groups[1].Value;
    }

    [Fact]
    public void Plan_lays_out_page_runtime_and_model()
    {
        var paths = ModelPageExporter.Plan(Model(), Options()).Select(f => f.Path).ToList();
        Assert.Contains("index.html", paths);
        Assert.Contains("README.txt", paths);
        Assert.Contains("assets/model-page.js", paths);
        Assert.Contains("assets/model-page.css", paths);
        Assert.Contains("assets/station-mark.js", paths);
        Assert.Contains($"caveview/{CaveViewRuntime.Version}/js/CaveView2.min.js", paths);
        Assert.Contains("model/Pestera-Mare.lox", paths);
        Assert.Equal(paths.Count, paths.Distinct().Count());
        Assert.DoesNotContain(paths, p => p.Contains('\\'));
    }

    [Fact]
    public void Index_has_no_placeholder_left_and_escapes_the_readers_text()
    {
        var opts = Options(o => o with { Title = "A & B </script><b>" });
        var html = ModelPageExporter.RenderIndex(opts, "cave.lox");
        Assert.DoesNotContain("{{", html);
        Assert.Contains("A &amp; B &lt;/script&gt;&lt;b&gt;", html);
        Assert.Contains("Line &lt;two&gt;", html);
        Assert.Contains($"src=\"caveview/{CaveViewRuntime.Version}/js/CaveView2.min.js\"", html);
        Assert.DoesNotContain("model/model.js", html);
    }

    [Fact]
    public void Config_cannot_close_its_script_element_and_round_trips()
    {
        var opts = Options(o => o with
        {
            Language = "ro",
            Places = new[] { new ModelPagePlace("</script> entrance", "cave.a.0", "note"), new ModelPagePlace("", "cave.b.3"), new ModelPagePlace("ignored", " ") },
            View = new ModelPageView { Shading = "survey", Splays = true, Orientation = "plan" },
        });
        var json = Config(ModelPageExporter.RenderIndex(opts, "cave.lox"));
        Assert.DoesNotContain("</", json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("ro", root.GetProperty("language").GetString());
        Assert.Equal("cave.lox", root.GetProperty("model").GetString());
        Assert.Equal("model/", root.GetProperty("surveyDirectory").GetString());
        Assert.Equal($"caveview/{CaveViewRuntime.Version}/", root.GetProperty("home").GetString());
        Assert.Equal("survey", root.GetProperty("view").GetProperty("shading").GetString());
        Assert.True(root.GetProperty("view").GetProperty("splays").GetBoolean());
        var places = root.GetProperty("places").EnumerateArray().ToList();
        Assert.Equal(2, places.Count);
        Assert.Equal("</script> entrance", places[0].GetProperty("label").GetString());
        Assert.Equal("cave.b.3", places[1].GetProperty("label").GetString());   // empty label → station
    }

    [Fact]
    public void Embedded_model_is_a_script_holding_the_bytes()
    {
        var files = ModelPageExporter.Plan(Model("cave.3d"), Options(o => o with { EmbedModel = true }));
        Assert.DoesNotContain(files, f => f.Path == "model/cave.3d");
        var script = files.Single(f => f.Path == "model/model.js");
        var text = new StreamReader(script.Open()).ReadToEnd();
        Assert.Contains("\"cave.3d\"", text);
        Assert.Contains(Convert.ToBase64String(FakeModel), text);
        var html = new StreamReader(files.Single(f => f.Path == "index.html").Open()).ReadToEnd();
        Assert.Contains("<script src=\"model/model.js\"></script>", html);
    }

    [Theory]
    [InlineData("cave.dxf")]
    [InlineData("cave")]
    public void Plan_refuses_a_model_caveview_cannot_read(string name) =>
        Assert.Throws<ArgumentException>(() => ModelPageExporter.Plan(Model(name), Options()));

    [Fact]
    public void Plan_refuses_an_empty_title_an_unknown_language_and_a_bad_span()
    {
        Assert.Throws<ArgumentException>(() => ModelPageExporter.Plan(Model(), Options(o => o with { Title = " " })));
        Assert.Throws<ArgumentException>(() => ModelPageExporter.Plan(Model(), Options(o => o with { Language = "xx" })));
        Assert.Throws<ArgumentException>(() => ModelPageExporter.Plan(Model(), Options(o => o with { CloseUpSpan = 0 })));
    }

    [Theory]
    [InlineData("Peștera Mare.LOX", "Pestera-Mare.lox")]
    [InlineData("../../etc/passwd.3d", "passwd.3d")]
    [InlineData("???.lox", "model.lox")]
    public void SafeFileName_keeps_a_url_safe_name(string input, string expected) =>
        Assert.Equal(expected, ModelPageExporter.SafeFileName(input));

    [Fact]
    public void Directory_and_zip_hold_the_same_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "therion-caveview-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var index = ModelPageExporter.WriteToDirectory(Model(), Options(), dir);
            Assert.True(File.Exists(index));
            Assert.Equal(FakeModel, File.ReadAllBytes(Path.Combine(dir, "model", "Pestera-Mare.lox")));

            using var ms = new MemoryStream();
            ModelPageExporter.WriteZip(Model(), Options(), ms);
            ms.Position = 0;
            using var zip = new ZipArchive(ms);
            var onDisk = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).OrderBy(x => x, StringComparer.Ordinal);
            Assert.Equal(onDisk, zip.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}

public class ModelPlacesTests
{
    private static Therion.Blender.CaveStation St(uint id, uint survey, string name, Therion.Blender.CaveStationFlags flags = 0, string? comment = null) =>
        new() { Id = id, SurveyId = survey, Name = name, Flags = flags, Comment = comment };

    [Fact]
    public void Lox_paths_leave_out_the_top_survey_and_keep_dotted_names_whole()
    {
        var model = new Therion.Blender.CaveModel
        {
            SourceFormat = Therion.Blender.CaveSourceFormat.Lox,
            Surveys = new[]
            {
                new Therion.Blender.CaveSurvey(0, 0, "", null),          // the file's own top survey
                new Therion.Blender.CaveSurvey(1, 0, "cave", null),
                new Therion.Blender.CaveSurvey(2, 1, "upper", null),
                new Therion.Blender.CaveSurvey(3, 9, "orphan", null),    // parent not read before it
            },
            Stations = new[]
            {
                St(10, 2, "N32.11", Therion.Blender.CaveStationFlags.Entrance),
                St(11, 2, "5", comment: "  Big chamber "),
                St(12, 3, "1", Therion.Blender.CaveStationFlags.Entrance),
                St(13, 2, "6"),                                          // neither: not listed
                St(14, 2, "", Therion.Blender.CaveStationFlags.Entrance), // anonymous: not listed
            },
        };
        var places = ModelPlaces.FromModel(model);
        Assert.Equal(new[] { "cave.upper.N32.11", "orphan.1", "cave.upper.5" }, places.Select(p => p.Station));
        Assert.Equal(new[] { "cave", "upper", "N32.11" }, places[0].StationParts);
        Assert.Equal("N32.11", places[0].Label);
        Assert.Equal("entrance", places[0].Note);
        Assert.Equal("Big chamber", places[2].Label);
        Assert.Null(places[2].Note);
    }

    [Fact]
    public void Survex_paths_are_the_labels_and_the_list_is_capped()
    {
        var model = new Therion.Blender.CaveModel
        {
            SourceFormat = Therion.Blender.CaveSourceFormat.Survex3d,
            Stations = Enumerable.Range(0, 5).Select(i => new Therion.Blender.CaveStation
            {
                Id = (uint)i, Name = $"cave.ent{i}", Flags = Therion.Blender.CaveStationFlags.Entrance,
            }).ToArray(),
        };
        var places = ModelPlaces.FromModel(model, max: 3);
        Assert.Equal(new[] { "cave.ent0", "cave.ent1", "cave.ent2" }, places.Select(p => p.Station));
    }

    [Fact]
    public void A_dotted_station_name_is_passed_to_the_page_as_its_names()
    {
        var place = new ModelPagePlace("x", "cave.N32.11") { StationParts = new[] { "cave", "N32.11" } };
        var json = ModelPageExporter.RenderConfig(new ModelPageOptions { Title = "t", Places = new[] { place } }, "c.lox");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement.GetProperty("places")[0].GetProperty("ref");
        Assert.Equal(new[] { "cave", "N32.11" }, r.EnumerateArray().Select(e => e.GetString()));
    }
}
