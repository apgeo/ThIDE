using Therion.Build.Automation;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

public class BatchBuildViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-batch-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Cfg(string name, params string[] exports)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "source cave.th\n" + string.Concat(exports.Select(e => e + "\n")));
        return path;
    }

    private (BatchBuildViewModel, List<(string Entry, string Text)>) Make(Func<string, bool>? succeeds = null, string? selection = null, params string[] configs)
    {
        var built = new List<(string, string)>();
        var env = new BatchBuildEnvironment(configs, _root,
            (entry, temp, label) =>
            {
                built.Add((entry, File.ReadAllText(entry)));
                if (temp is not null) File.Delete(temp);
                var ok = succeeds?.Invoke(entry) ?? true;
                return Task.FromResult<BuildOutcome?>(new BuildOutcome(entry, ok, false, ok ? 0 : 1, ok ? 0 : 1, 0, [], null, TimeSpan.FromSeconds(1), label));
            },
            () => { }, null, selection);
        return (new BatchBuildViewModel(env), built);
    }

    [Fact]
    public async Task Whole_thconfigs_build_as_they_are_and_partial_ones_through_a_trimmed_copy()
    {
        var a = Cfg("a.thconfig", "export map -o a.pdf", "export model -o a.lox");
        var b = Cfg("b.thconfig");
        var (vm, built) = Make(configs: [a, b]);
        vm.Configs[0].Targets[1].IsChecked = false;   // a: map only

        await vm.RunAsync();

        Assert.Equal(2, built.Count);
        Assert.StartsWith(".thide-batch-", Path.GetFileName(built[0].Entry));
        Assert.Contains("\nexport map -o a.pdf", built[0].Text);
        Assert.Contains("# export model -o a.lox", built[0].Text);
        Assert.Equal(b, built[1].Entry);
        Assert.False(File.Exists(built[0].Entry));     // the temporary copy is gone
        Assert.Equal([a, b], vm.Outcomes.Select(o => o.Entry)); // reported under the real thconfig
    }

    [Fact]
    public async Task Stop_on_failure_skips_the_rest()
    {
        var a = Cfg("a.thconfig");
        var b = Cfg("b.thconfig");
        var (vm, built) = Make(e => !e.EndsWith("a.thconfig"), null, a, b);
        vm.StopOnFailure = true;

        await vm.RunAsync();

        Assert.Single(built);
        Assert.Contains("0 of 2", vm.Status);
    }

    [Fact]
    public async Task The_selection_is_remembered()
    {
        var a = Cfg("a.thconfig", "export map -o a.pdf", "export model -o a.lox");
        var b = Cfg("b.thconfig");
        var file = Path.Combine(_root, "sel", "batch.json");
        var (vm, _) = Make(null, file, a, b);
        vm.Configs[1].IsChecked = false;
        vm.Configs[0].Targets[0].IsChecked = false;
        await vm.RunAsync();

        var (again, _) = Make(null, file, a, b);

        Assert.True(again.Configs[0].IsChecked);
        Assert.False(again.Configs[1].IsChecked);
        Assert.False(again.Configs[0].Targets[0].IsChecked);
        Assert.True(again.Configs[0].Targets[1].IsChecked);
    }
}
