// Pre-compile output-folder check: the directories of each export's `-o` path (resolved against the
// build working dir) are what BuildViewModel ensures exist before Therion runs. These cover the path
// resolution; directory creation itself is a thin Directory.CreateDirectory wrapper around the result.

using System.Linq;
using TherionProc.ViewModels;
using Xunit;

namespace TherionProc.Tests;

public class OutputDirectoryResolutionTests
{
    private const string WorkDir = @"C:\proj";

    [Fact]
    public void Relative_output_subfolder_resolves_against_the_work_dir()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export model -o output/cave.lox\n", WorkDir);
        Assert.Equal(new[] { @"C:\proj\output" }, dirs);
    }

    [Fact]
    public void Forward_and_back_slashes_and_nesting_normalize()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export map -o exports/maps/plan.pdf\n", WorkDir);
        Assert.Equal(new[] { @"C:\proj\exports\maps" }, dirs);
    }

    [Fact]
    public void Bare_filename_maps_to_the_work_dir_itself()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export model -o cave.lox\n", WorkDir);
        Assert.Equal(new[] { @"C:\proj" }, dirs);
    }

    [Fact]
    public void Rooted_output_path_keeps_its_own_directory()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export model -o C:/abs/out/m.lox\n", WorkDir);
        Assert.Equal(new[] { @"C:\abs\out" }, dirs);
    }

    [Fact]
    public void Duplicate_target_directories_are_collapsed()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export model -o out/a.lox\nexport map -o out/b.pdf\n", WorkDir);
        Assert.Equal(new[] { @"C:\proj\out" }, dirs);
    }

    [Fact]
    public void Exports_without_an_output_option_are_ignored()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export model\n", WorkDir);
        Assert.Empty(dirs);
    }

    [Fact]
    public void Multiple_distinct_directories_are_all_returned()
    {
        var dirs = BuildViewModel.ResolveExportOutputDirectories(
            "export model -o models/cave.lox\nexport map -o maps/plan.pdf\n", WorkDir);
        Assert.Equal(new[] { @"C:\proj\models", @"C:\proj\maps" }, dirs.ToArray());
    }
}
