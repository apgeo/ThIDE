using Therion.Cli;
using Therion.CaveGeometry.Writers;
using Therion.MeshSurvey.Tests.Synthetic;

namespace Therion.Cli.Tests;

public class MeshSurveyVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-cli-mesh-").FullName;
    private readonly StringWriter _out = new(), _err = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Model(SyntheticCave cave, string name = "cave.stl")
    {
        var path = Path.Combine(_root, name);
        StlWriter.WriteFile(cave.Mesh(0.25), path);
        return path;
    }

    private int Run(params string[] args) => MeshSurveyVerb.Run(["mesh-survey", .. args], _out, _err);

    [Fact]
    public void Inspect_reports_size_holes_loops_and_memory()
    {
        var model = Model(SyntheticCave.Loop());
        Assert.Equal(0, Run("inspect", model, "--units", "m"));
        var text = _out.ToString();
        Assert.Contains("units        metres", text);
        Assert.Contains("holes        0", text);
        Assert.Contains("loops        1", text);
        Assert.Contains("void         voxel", text);
    }

    [Fact]
    public void Check_leg_says_clear_or_blocked()
    {
        var model = Model(SyntheticCave.Bend());
        Assert.Equal(0, Run("check-leg", model, "--units", "m", "--from", "2,0,0", "--to", "12,0,0"));
        Assert.StartsWith("clear", _out.ToString());
        Assert.Equal(1, Run("check-leg", model, "--units", "m", "--from", "6,0,0", "--to", "20,7,-0.8"));
        Assert.Contains("blocked", _out.ToString());
    }

    [Fact]
    public void Run_writes_the_survey_and_its_report_and_refuses_to_overwrite()
    {
        var model = Model(SyntheticCave.YJunction());
        var th = Path.Combine(_root, "y.th");
        Assert.Equal(0, Run("run", model, "--units", "m", "--out", th, "--voxel", "0.25", "--preferred", "6", "--splays", "ring:6", "--debug"));
        var text = File.ReadAllText(th);
        Assert.Contains("DERIVED, NOT MEASURED", text);
        Assert.Contains("data normal from to length compass clino", text);
        Assert.True(File.Exists(Path.Combine(_root, "y.mesh-survey.json")));
        Assert.True(File.Exists(Path.Combine(_root, "y.skeleton.ply")));
        Assert.True(File.Exists(Path.Combine(_root, "y.preview.lox")));
        Assert.Contains("coverage", _out.ToString());
        Assert.Equal(1, Run("run", model, "--units", "m", "--out", th, "--voxel", "0.25"));
        Assert.Contains("exists", _err.ToString());
    }

    [Fact]
    public void A_guide_file_and_a_start_point()
    {
        var model = Model(SyntheticCave.Straight(20, 1.5));
        var guide = Path.Combine(_root, "guide.json");
        File.WriteAllText(guide, """
            { "start": { "name": "a", "position": { "x": 3, "y": 0, "z": 0 } },
              "points": [ { "name": "b", "position": { "x": 9, "y": 0, "z": 0 } } ],
              "legs": [ { "from": "a", "to": "b" } ] }
            """);
        var th = Path.Combine(_root, "g.th");
        Assert.Equal(0, Run("run", model, "--units", "m", "--out", th, "--voxel", "0.25", "--guide", guide));
        Assert.Contains("fix ", File.ReadAllText(th));
        Assert.Equal(1, Run("run", model, "--units", "m", "--out", Path.Combine(_root, "h.th"), "--voxel", "0.25", "--start", "3,0,5"));
    }

    [Fact]
    public void Usage_errors_exit_2()
    {
        var model = Model(SyntheticCave.Straight());
        Assert.Equal(2, Run("run", model, "--out", Path.Combine(_root, "x.th"), "--bogus"));
        Assert.Equal(2, Run("run", model, "--out", Path.Combine(_root, "x.th"), "--strategy", "sideways"));
        Assert.Equal(2, Run("explode", model));
        Assert.Equal(2, MeshSurveyVerb.Run(["mesh-survey"], _out, _err));
        Assert.Equal(2, Run("run", model, "--out", Path.Combine(_root, "x.th"), "--preset", "nope"));
    }
}
