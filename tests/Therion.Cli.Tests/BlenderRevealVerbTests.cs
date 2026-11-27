// `therion-cli blender <model> --reveal …`: the cave appears part by part, from the command line,
// on invented caves. What the options write is read back from a render bundle's plan and mesh;
// what they cannot be used with is an error before anything is written. No Blender is run.

using System.IO.Compression;
using System.Text;
using Therion.Blender.Plan;
using Therion.Blender.Tests;
using Therion.Blender.Tests.SceneProbe;
using Therion.Cave3D.Parsing;
using Therion.Cli;

namespace Therion.Cli.Tests;

public class BlenderRevealVerbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-cli-reveal-" + Guid.NewGuid().ToString("N"));
    private readonly string _lox;

    public BlenderRevealVerbTests()
    {
        Directory.CreateDirectory(_dir);
        _lox = Path.Combine(_dir, "invented.lox");
        LoxWriter.WriteFile(InventedCave.Build(InventedCaveSize.Small), _lox);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var so = new StringWriter();
        var se = new StringWriter();
        int code = await BlenderVerb.RunAsync(args, so, se);
        return (code, so.ToString(), se.ToString());
    }

    private static (ScenePlan Plan, string MeshHeader) Bundled(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        string top = Path.GetFileNameWithoutExtension(zip) + "/";
        using var plan = new StreamReader(archive.GetEntry(top + "scene-plan.json")!.Open());
        using var mesh = archive.GetEntry(top + "model.ply")!.Open();
        var header = new byte[400];
        int read = mesh.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return (ScenePlanSerializer.Read(plan.ReadToEnd()), Encoding.ASCII.GetString(header, 0, read));
    }

    /// <summary>A cave of three legs along X in whole metres, as a Survex .3d: the first two
    /// surveyed in the summer of 1987, the third in 2003. Invented.</summary>
    private string Dated3d(string name = "dated.3d")
    {
        int Days(int year, int month, int day) => new DateOnly(year, month, day).DayNumber - new DateOnly(1900, 1, 1).DayNumber;
        var file = Survex3dFixtureBuilder.V8("Invented").V8Style(0x00)
            .V8SingleDate(Days(1987, 7, 2))
            .V8Move(0, 0, 0).V8Line(0, (0, "cave"), 10, 0, 0).V8Line(0, null, 20, 0, 0)
            .V8SingleDate(Days(2003, 1, 1))
            .V8Line(0, null, 30, 0, 0)
            .V8Label(0x02 | 0x04, 4, "in", 0, 0, 0)
            .V8Label(0x02, 2, "end", 30, 0, 0)
            .V8Stop();
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, file.Build());
        return path;
    }

    /// <summary>The cave of <see cref="Dated3d"/> as a .lox has it: stations with ids, legs
    /// between them, no dates.</summary>
    private static Therion.Cave3D.CaveModel SameCaveWithoutDates()
    {
        var stations = Enumerable.Range(0, 4)
            .Select(i => new Therion.Cave3D.CaveStation
            {
                Id = (uint)i, SurveyId = 0, Name = "s" + i, Position = new Therion.Cave3D.CaveVector3(10.0 * i, 0, 0),
                Flags = i == 0 ? Therion.Cave3D.CaveStationFlags.Entrance : Therion.Cave3D.CaveStationFlags.None, RawFlags = i == 0 ? 2u : 0u,
            })
            .ToList();
        return new Therion.Cave3D.CaveModel
        {
            SourceFormat = Therion.Cave3D.CaveSourceFormat.Lox,
            Surveys = [new Therion.Cave3D.CaveSurvey(0, 0, "", "Invented")],
            Stations = stations,
            Shots = Enumerable.Range(0, 3)
                .Select(i => new Therion.Cave3D.CaveShot
                {
                    FromStationId = (uint)i, ToStationId = (uint)i + 1, FromPosition = stations[i].Position, ToPosition = stations[i + 1].Position,
                    SurveyId = 0, SectionType = Therion.Cave3D.CaveShotSection.Oval,
                    FromLrud = new Therion.Cave3D.CaveLrud(1, 1, 1, 1), ToLrud = new Therion.Cave3D.CaveLrud(1, 1, 1, 1), Threshold = 60,
                })
                .ToList(),
        };
    }

    [Fact]
    public async Task Reveal_ByDistance_IsAChangeOfThePlan_OverTheValuesInTheMesh()
    {
        string zip = Path.Combine(_dir, "distance.zip");

        var (code, _, stderr) = await Run("blender", _lox, "--bundle", zip, "--reveal", "distance", "--reveal-seconds", "1-9.5");

        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("warning", stderr);
        var (plan, mesh) = Bundled(zip);
        Assert.Contains("property float reveal\n", mesh);
        var change = Assert.Single(plan.Changes!);
        Assert.Equal("CaveModel", change.Target!.Id);
        Assert.Equal((1.0, 0.0, 9.5), (change.Reveal![0].T, change.Reveal[0].Value, change.Reveal[1].T));
        Assert.True(change.Reveal[1].Value > 50, "the threshold ends past the far end of the cave");
        Assert.NotNull(change.RevealFront!.Glow);
        Assert.Contains("changes", plan.Features);
        // A reveal by distance has no years to count: the only text is the cave's name, which
        // the preset writes, and an empty title takes that away too.
        Assert.Equal(["title"], plan.Texts!.Items.Select(i => i.Role));
        string untitled = Path.Combine(_dir, "untitled.zip");
        (code, _, stderr) = await Run("blender", _lox, "--bundle", untitled, "--reveal", "distance", "--title", "");
        Assert.True(code == 0, stderr);
        Assert.Null(Bundled(untitled).Plan.Texts);

        // Measured from a station of the model, the values start there.
        string from = Path.Combine(_dir, "from.zip");
        (code, _, stderr) = await Run("blender", _lox, "--bundle", from, "--reveal", "Distance", "--reveal-from", "side3");
        Assert.True(code == 0, stderr);
        Assert.NotEqual(change.Reveal[1].Value, Bundled(from).Plan.Changes![0].Reveal![1].Value);
    }

    [Fact]
    public async Task Reveal_ByDate_OfA3d_HasACounterOfItsYears_AndALoxFindsTheDatesBesideItOrIsToldWhere()
    {
        string dated = Dated3d();
        string zip = Path.Combine(_dir, "date.zip");

        var (code, _, stderr) = await Run("blender", dated, "--bundle", zip, "--reveal", "date");

        Assert.True(code == 0, stderr);
        var (plan, mesh) = Bundled(zip);
        Assert.Contains("property float reveal\n", mesh);
        var change = Assert.Single(plan.Changes!);
        Assert.Equal(1987 + 182 / 365.0, change.Reveal![0].Value, 5);
        Assert.InRange(change.Reveal[1].Value, 2003, 2004.5);
        // The cave's name, which the preset writes, and the counter of the years.
        Assert.Equal(["title", "reveal-counter"], plan.Texts!.Items.Select(i => i.Role));
        var counter = plan.Texts.Items[1];
        Assert.Equal(("{}", 1987.0, 2003.0), (counter.Text, counter.Counter!.Min, counter.Counter.Max));
        Assert.Equal(["models", "camera_track", "look", "labels", "changes", "texts", "output"], plan.Features);

        // A .lox has no dates: without a .3d it is rendered whole, and says so.
        string bare = Path.Combine(_dir, "bare.zip");
        (code, _, stderr) = await Run("blender", _lox, "--bundle", bare, "--reveal", "date");
        Assert.True(code == 0, stderr);
        Assert.Contains("warning: The model has no survey dates to reveal it by", stderr);
        Assert.Null(Bundled(bare).Plan.Changes);
        Assert.DoesNotContain("reveal", Bundled(bare).MeshHeader);

        // The same cave as a .lox, with its .3d named: the dates are matched leg by leg.
        string walls = Path.Combine(_dir, "walls.lox");
        LoxWriter.WriteFile(SameCaveWithoutDates(), walls);
        string named = Path.Combine(_dir, "named.zip");
        (code, _, stderr) = await Run("blender", walls, "--bundle", named, "--reveal", "date", "--reveal-dates", dated);
        Assert.True(code == 0, stderr);
        Assert.DoesNotContain("warning", stderr);
        Assert.Equal(change.Reveal[0].Value, Bundled(named).Plan.Changes![0].Reveal![0].Value);
        // And with a .3d of its own name beside it, nothing needs naming.
        File.Copy(dated, Path.Combine(_dir, "walls.3d"));
        string beside = Path.Combine(_dir, "beside.zip");
        (code, _, stderr) = await Run("blender", walls, "--bundle", beside, "--reveal", "date");
        Assert.True(code == 0, stderr);
        Assert.Equal(change.Reveal, Bundled(beside).Plan.Changes![0].Reveal);
    }

    [Fact]
    public async Task TheMapRevealPreset_Reveals_AndAnExportedScriptSaysItDoesNot()
    {
        string zip = Path.Combine(_dir, "map.zip");
        var (code, _, stderr) = await Run("blender", _lox, "--bundle", zip, "--preset", "Map reveal");
        Assert.True(code == 0, stderr);
        Assert.Contains(Bundled(zip).Plan.Changes!, c => c.Reveal is not null);

        // Another kind of reveal over the preset's keeps its times.
        string order = Path.Combine(_dir, "order.zip");
        (code, _, stderr) = await Run("blender", _lox, "--bundle", order, "--preset", "Map reveal", "--reveal", "order");
        Assert.True(code == 0, stderr);
        var keys = Bundled(order).Plan.Changes!.Single(c => c.Reveal is not null).Reveal!;
        Assert.Equal((0.5, 0.0, 9.5), (keys[0].T, keys[0].Value, keys[1].T));
        Assert.InRange(keys[1].Value, 16, 17.5);

        string folder = Path.Combine(_dir, "exported");
        (code, _, stderr) = await Run("blender", _lox, "--export", folder, "--builder", "generator", "--reveal", "distance");
        Assert.True(code == 0, stderr);
        Assert.Contains("warning: the exported script does not show reveal", stderr);
    }

    [Theory]
    [InlineData(new[] { "--reveal", "depth" }, "--reveal takes date, order or distance, not 'depth'")]
    [InlineData(new[] { "--reveal", "1" }, "--reveal takes date, order or distance, not '1'")]
    [InlineData(new[] { "--reveal-from", "main0" }, "go with a reveal (--reveal date|order|distance)")]
    [InlineData(new[] { "--reveal", "order", "--reveal-from", "main0" }, "--reveal-from names the station a reveal by distance is measured from")]
    [InlineData(new[] { "--reveal", "distance", "--reveal-dates", "x.3d" }, "--reveal-dates names the file with the survey dates of a reveal by date")]
    [InlineData(new[] { "--reveal", "date", "--reveal-dates", "nowhere.3d" }, "file not found: nowhere.3d")]
    [InlineData(new[] { "--reveal", "distance", "--reveal-seconds", "soon" }, "--reveal-seconds takes \"<start>-<end>\" in seconds")]
    [InlineData(new[] { "--reveal", "distance", "--reveal-seconds", "5-2" }, "reveal.end: The reveal ends after it starts.")]
    [InlineData(new[] { "--reveal", "distance", "--reveal-seconds", "40-50" }, "reveal.start: The reveal starts before the clip ends.")]
    [InlineData(new[] { "--reveal", "distance", "--reveal-from", "no.such" }, "The model has no station 'no.such' to measure the reveal from.")]
    public async Task ARevealThatCannotBeMade_IsAnError_AndWritesNoBundle(string[] options, string said)
    {
        string zip = Path.Combine(_dir, "refused.zip");

        var (code, _, stderr) = await Run(["blender", _lox, "--bundle", zip, .. options]);

        Assert.Equal(2, code);
        Assert.Contains(said, stderr);
        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task AFileOfDatesThatIsNoModel_IsNamed_AndNothingOfItIsQuoted()
    {
        string notes = Path.Combine(_dir, "notes.3d");
        File.WriteAllText(notes, "INVENTED-SECRET-LINE: not a model at all");
        string zip = Path.Combine(_dir, "refused.zip");

        var (code, _, stderr) = await Run("blender", _lox, "--bundle", zip, "--reveal", "date", "--reveal-dates", notes);

        Assert.Equal(2, code);
        Assert.Contains("reveal.datesPath: 'notes.3d' cannot be read as a cave model", stderr);
        Assert.DoesNotContain("INVENTED-SECRET-LINE", stderr);
        Assert.False(File.Exists(zip));
    }
}
