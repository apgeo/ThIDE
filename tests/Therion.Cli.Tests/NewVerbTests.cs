using Therion.Cli;

namespace Therion.Cli.Tests;

public class NewVerbTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-cli-new-").FullName;
    private readonly StringWriter _out = new(), _err = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private int Run(params string[] args) => NewVerb.Run(["new", .. args], _out, _err, Path.Combine(_root, "lib"), new DateTime(2026, 10, 28));

    [Fact]
    public void Lists_the_built_ins()
    {
        Assert.Equal(0, Run("--list"));
        Assert.Contains("single-cave", _out.ToString());
        Assert.Contains("expedition", _out.ToString());
    }

    [Fact]
    public void Creates_a_project_with_values()
    {
        var target = Path.Combine(_root, "out");

        Assert.Equal(0, Run("expedition", "--out", target, "--var", "expedition=alba", "--var", "year=2025"));

        Assert.True(File.Exists(Path.Combine(target, "alba-2025.thconfig")));
        Assert.Contains("thconfig: ", _out.ToString());
    }

    [Fact]
    public void Bad_value_and_missing_out_are_usage_errors()
    {
        Assert.Equal(2, Run("single-cave", "--out", Path.Combine(_root, "x"), "--var", "project=two words"));
        Assert.Equal(2, Run("single-cave"));
        Assert.Equal(2, Run("nope", "--out", _root));
        Assert.Contains("error:", _err.ToString());
    }
}
