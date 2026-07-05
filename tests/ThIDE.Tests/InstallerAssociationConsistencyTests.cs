using System;
using System.IO;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// The Windows (Inno Setup) and Linux (.deb, AppImage) installers register the Therion file types,
/// and those lists are hand-authored in build/**. This guards them against silently drifting from the
/// single source of truth — <see cref="FileAssociationCatalog.Types"/>: if someone adds/removes a type
/// there without updating the installers, these tests fail. See build/windows/ThIDE.iss,
/// build/linux/build-deb.sh and build/linux/build-appimage.sh.
/// </summary>
public class InstallerAssociationConsistencyTests
{
    // Walk up from the test binary until we find the repo root (the folder holding ThIDE.sln).
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadBuildFile(params string[] relative)
    {
        var path = Path.Combine(RepoRoot(), Path.Combine(relative));
        Assert.True(File.Exists(path), $"expected installer file at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void InnoSetupScript_registers_every_catalog_extension()
    {
        var iss = ReadBuildFile("build", "windows", "ThIDE.iss");
        foreach (var (ext, _) in FileAssociationCatalog.Types)
        {
            // The extension key itself, e.g.  Software\Classes\.th2"  (trailing quote pins the exact ext).
            Assert.True(iss.Contains($@"Software\Classes\{ext}""", StringComparison.Ordinal),
                $"ThIDE.iss is missing the extension key for '{ext}'.");
            // And its ProgId,  e.g.  ThIDE.th2
            Assert.True(iss.Contains($"ThIDE{ext}", StringComparison.Ordinal),
                $"ThIDE.iss is missing the ProgId 'ThIDE{ext}'.");
        }
    }

    // Both Linux installers declare the file types in an identical EXTS bash array ("ext|description").
    [Theory]
    [InlineData("build-deb.sh")]
    [InlineData("build-appimage.sh")]
    public void LinuxBuildScript_registers_every_catalog_extension(string script)
    {
        var sh = ReadBuildFile("build", "linux", script);
        foreach (var (ext, _) in FileAssociationCatalog.Types)
        {
            var bare = ext.TrimStart('.');           // ".th2" -> "th2"
            // Matches the EXTS array entry, e.g.  "th2|Therion 2D map / scrap"
            Assert.True(sh.Contains($"\"{bare}|", StringComparison.Ordinal),
                $"{script} is missing the file type '{ext}' in its EXTS list.");
        }
    }
}
