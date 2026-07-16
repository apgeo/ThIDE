// derived-thconfig generation + quick-export block composition.

using System.Linq;
using Therion.Syntax;
using ThIDE.Services;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class QuickExportTests
{
    private const string Thconfig = """
        source cave.th
        export model -fmt loch -o cave.lox
        export map -projection plan -fmt pdf -o cave.pdf
        """;

    [Fact]
    public void ParseExports_finds_each_export()
    {
        var exports = ThconfigExportEditor.ParseExports(Thconfig);
        Assert.Equal(2, exports.Count);
        Assert.Contains(exports, e => e.Format == "loch");
        Assert.Contains(exports, e => e.Format == "pdf");
    }

    [Fact]
    public void IsolateExport_comments_out_the_other_exports()
    {
        var exports = ThconfigExportEditor.ParseExports(Thconfig);
        var pdf = exports.First(e => e.Format == "pdf");
        var result = ThconfigExportEditor.IsolateExport(Thconfig, pdf);

        Assert.Contains("# export model -fmt loch", result);          // the other one is disabled
        Assert.Contains("export map -projection plan -fmt pdf", result);
        Assert.DoesNotContain("# export map", result);                // the kept one stays active
        Assert.Contains("source cave.th", result);                    // context preserved
    }

    [Fact]
    public void ComposeExport_disables_existing_and_appends_block()
    {
        var result = ThconfigExportEditor.ComposeExport(Thconfig, "export model -fmt kml -o out.kml");
        Assert.Contains("# export model -fmt loch", result);
        Assert.Contains("# export map", result);
        Assert.Contains("export model -fmt kml -o out.kml", result);
    }

    [Fact]
    public void QuickExport_map_block_includes_projection_scale_and_output()
    {
        var vm = new QuickExportViewModel
        {
            SelectedFormat = new QuickExportViewModel.ExportFormat("PDF map", true, "pdf"),
            SelectedProjection = "extended",
            Survey = "cave.upper",
            UseScale = true,
            Scale = 500,
            OutputName = "plan",
        };
        var block = vm.ComposeBlock();

        Assert.StartsWith("select cave.upper", block);
        Assert.Contains("scale 1 500", block);
        Assert.Contains("export map -projection extended -fmt pdf -layout _tp_quickexport", block);
        Assert.Contains("\"plan.pdf\"", block);
    }

    [Fact]
    public void QuickExport_model_block_has_no_projection()
    {
        var vm = new QuickExportViewModel
        {
            SelectedFormat = new QuickExportViewModel.ExportFormat("Loch", false, "loch"),
            OutputName = "model",
        };
        var block = vm.ComposeBlock();
        // .lox is not stored on the preset — it is derived from `loch` via the shared vocabulary.
        Assert.Equal("export model -fmt loch -o \"model.lox\"", block);
    }

    [Fact]
    public void Every_preset_offers_a_format_the_compiler_actually_accepts()
    {
        // The dialog's -fmt tokens are Therion's, so they are checked against Therion's own table
        // rather than trusted. This is the guard against the `lox`-for-`loch` class of mistake: a
        // preset naming a format the compiler rejects would produce a thconfig that cannot build.
        foreach (var f in new QuickExportViewModel().Formats)
        {
            Assert.True(CommandVocabulary.IsExportType(f.ExportType), $"{f.Display}: bad export type");
            Assert.True(CommandVocabulary.IsExportFormat(f.ExportType, f.Fmt),
                $"{f.Display}: '-fmt {f.Fmt}' is not valid for 'export {f.ExportType}'");
        }
    }

    [Fact]
    public void Preset_extensions_come_from_the_shared_vocabulary_not_a_second_table()
    {
        var presets = new QuickExportViewModel().Formats;

        Assert.Equal(".lox", presets.Single(f => f.Fmt == "loch").Ext);   // the pair that differs
        Assert.Equal(".3d", presets.Single(f => f.Fmt == "survex").Ext);
        Assert.Equal(".pdf", presets.Single(f => f.Fmt == "pdf").Ext);    // identity case

        // Every preset's extension resolves back to a format that writes that same file. Not
        // necessarily the same NAME: .3d resolves to `3d`, not `survex`, because both write it and
        // `3d` is the one a caller would type. Asserting name equality would be asserting a bijection
        // that Therion's format table does not have.
        foreach (var f in presets)
        {
            var back = CommandVocabulary.ExportFormatForExtension(f.ExportType, f.Ext);
            Assert.True(back is not null, $"{f.Display}: {f.Ext} resolves to no format");
            Assert.Equal(f.Ext, CommandVocabulary.ExtensionForExportFormat(back!));
        }
    }
}
