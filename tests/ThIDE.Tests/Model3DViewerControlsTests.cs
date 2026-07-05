// 3D viewer (extensions) — pure logic for the model dropdown and the "generated" notice:
//   * BuildModelOptions: only viewable (.lox/.3d) `export model` outputs, in source order, with
//     relative-path resolution, existence flagging, detected-file append, and de-duplication.
//   * GeneratedNoticeFor: the "Recompile for a fresh model" notice only for non-today files.

using System;
using System.IO;
using System.Linq;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class Model3DViewerControlsTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "thproj");

    private static string Full(params string[] parts) => Path.GetFullPath(Path.Combine(new[] { Dir }.Concat(parts).ToArray()));

    [Fact]
    public void Lists_only_viewable_model_exports_in_source_order()
    {
        const string text = """
            source cave.th
            export model -fmt lox -o out/cave.lox
            export model -fmt survex -o out/cave.3d
            export model -fmt kml -o out/cave.kml
            export map -projection plan -o out/cave.pdf
            """;
        var opts = Model3DViewerViewModel.BuildModelOptions(text, Dir, _ => false, Array.Empty<string>());

        Assert.Equal(2, opts.Count);                                   // kml + map excluded
        Assert.Equal("cave.lox", Path.GetFileName(opts[0].Path));
        Assert.Equal("cave.3d", Path.GetFileName(opts[1].Path));
        Assert.All(opts, o => Assert.False(o.Exists));                 // fileExists => false
        Assert.Contains("not built", opts[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Flags_existing_outputs_and_appends_detected_files()
    {
        const string text = "export model -o rez/a.lox\n";
        var aFull = Full("rez", "a.lox");
        var stray = Full("stray.3d");

        var opts = Model3DViewerViewModel.BuildModelOptions(text, Dir, p => p == aFull, new[] { stray });

        Assert.Equal(2, opts.Count);
        Assert.True(opts[0].Exists);
        Assert.DoesNotContain("not built", opts[0].Title, StringComparison.Ordinal);
        Assert.Equal("stray.3d", Path.GetFileName(opts[1].Path));      // detected extra appended
        Assert.True(opts[1].Exists);
    }

    [Fact]
    public void Resolves_backslash_and_dot_relative_outputs()
    {
        const string text = "export model -output .\\rez\\Av.lox\nexport model -output ./rez/Bv.3d\n";
        var opts = Model3DViewerViewModel.BuildModelOptions(text, Dir, _ => false, Array.Empty<string>());

        Assert.Equal(2, opts.Count);
        Assert.Equal("Av.lox", Path.GetFileName(opts[0].Path));
        Assert.Equal("Bv.3d", Path.GetFileName(opts[1].Path));
        Assert.Equal(Full("rez", "Av.lox"), opts[0].Path);
    }

    [Fact]
    public void Deduplicates_export_output_that_is_also_a_detected_file()
    {
        const string text = "export model -o cave.lox\n";
        var caveFull = Full("cave.lox");

        var opts = Model3DViewerViewModel.BuildModelOptions(text, Dir, _ => true, new[] { caveFull });

        Assert.Single(opts);
        Assert.Equal(caveFull, opts[0].Path);
    }

    [Fact]
    public void GeneratedNotice_empty_for_a_file_written_today()
    {
        var f = Path.GetTempFileName();   // just created → today
        try { Assert.Equal(string.Empty, Model3DViewerViewModel.GeneratedNoticeFor(f)); }
        finally { File.Delete(f); }
    }

    [Fact]
    public void GeneratedNotice_prompts_recompile_for_an_old_file()
    {
        var f = Path.GetTempFileName();
        try
        {
            File.SetLastWriteTime(f, DateTime.Now.AddDays(-3));
            var notice = Model3DViewerViewModel.GeneratedNoticeFor(f);
            Assert.Contains("Recompile for a fresh model", notice, StringComparison.Ordinal);
        }
        finally { File.Delete(f); }
    }
}
