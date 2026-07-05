// derived-thconfig generation + quick-export block composition.

using System.Linq;
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
            SelectedFormat = new QuickExportViewModel.ExportFormat("PDF map", true, "pdf", ".pdf"),
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
            SelectedFormat = new QuickExportViewModel.ExportFormat("Loch", false, "loch", ".lox"),
            OutputName = "model",
        };
        var block = vm.ComposeBlock();
        Assert.Equal("export model -fmt loch -o \"model.lox\"", block);
    }
}
