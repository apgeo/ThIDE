using System;
using System.IO;
using System.Linq;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>The project walk behind Replace in Files and Go to File stays inside the folder it starts in.</summary>
public class ProjectFileWalkTests
{
    [Fact]
    public void Finds_matching_files_in_real_subfolders()
    {
        using var project = new TempDir();
        var top = project.Write("a.th");
        var deep = project.Write("x/y/b.th");
        project.Write("x/notes.txt");

        var found = ProjectFileWalk.EnumerateFiles(project.Path, "*.th").ToHashSet();

        Assert.Equal(new[] { top, deep }.ToHashSet(), found);
    }

    [Fact]
    public void Does_not_descend_into_a_symlinked_folder_or_loop_on_one()
    {
        if (OperatingSystem.IsWindows()) return;   // directory symlinks need privileges there

        using var project = new TempDir();
        using var elsewhere = new TempDir();
        var inside = project.Write("a.th");
        elsewhere.Write("other.th");
        Directory.CreateSymbolicLink(Path.Combine(project.Path, "extra"), elsewhere.Path);
        Directory.CreateSymbolicLink(Path.Combine(project.Path, "loop"), project.Path);

        var found = ProjectFileWalk.EnumerateFiles(project.Path, "*").ToList();

        Assert.Equal([inside], found);
    }
}
