// Quick Export preset dialog VM. Composes a Therion export block (model vs map, format,
// projection, optional survey selection + scale) that the build pipeline appends to a temporary
// thconfig. Pure string building, so the composition is unit-testable.

using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Syntax;

namespace ThIDE.ViewModels;

public sealed partial class QuickExportViewModel : ObservableObject
{
    /// <summary>A selectable output format: model vs map, plus the Therion <c>-fmt</c> token.</summary>
    public sealed record ExportFormat(string Display, bool IsMap, string Fmt)
    {
        /// <summary>
        /// The extension this format writes. Derived, not stored: the format→extension pairing is
        /// Therion's, and it is only surprising where the two differ (<c>loch</c> writes <c>.lox</c>),
        /// which is exactly where a second copy would drift.
        /// </summary>
        public string Ext => CommandVocabulary.ExtensionForExportFormat(Fmt);

        /// <summary>The export type this format belongs to — what <c>-fmt</c> is validated against.</summary>
        public string ExportType => IsMap ? "map" : "model";

        public override string ToString() => Display;
    }

    /// <summary>
    /// The presets offered in the dialog — a curated few, not every format Therion accepts. The
    /// <c>Fmt</c> tokens are Therion's own, checked against <see cref="CommandVocabulary"/> by test.
    /// </summary>
    public IReadOnlyList<ExportFormat> Formats { get; } = new[]
    {
        new ExportFormat("PDF map", true, "pdf"),
        new ExportFormat("SVG map", true, "svg"),
        new ExportFormat("Loch 3D model (.lox)", false, "loch"),
        new ExportFormat("Survex 3D model (.3d)", false, "survex"),
        new ExportFormat("KML model", false, "kml"),
    };

    public IReadOnlyList<string> Projections { get; } = new[] { "plan", "extended", "elevation" };

    [ObservableProperty] private ExportFormat _selectedFormat;
    [ObservableProperty] private string _selectedProjection = "plan";
    [ObservableProperty] private string _survey = string.Empty;
    [ObservableProperty] private bool _useScale;
    [ObservableProperty] private int _scale = 500;
    [ObservableProperty] private string _outputName = "quick-export";

    public bool IsMap => SelectedFormat?.IsMap ?? false;
    partial void OnSelectedFormatChanged(ExportFormat value) => OnPropertyChanged(nameof(IsMap));

    public QuickExportViewModel()
    {
        _selectedFormat = Formats[0];
    }

    /// <summary>The output file name (relative; Therion resolves it against the thconfig folder).</summary>
    public string OutputFileName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(OutputName) ? "quick-export" : OutputName.Trim();
            return name.EndsWith(SelectedFormat.Ext, System.StringComparison.OrdinalIgnoreCase)
                ? name : name + SelectedFormat.Ext;
        }
    }

    /// <summary>Builds the Therion export block to append to the derived thconfig.</summary>
    public string ComposeBlock()
    {
        var f = SelectedFormat;
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Survey)) sb.Append("select ").Append(Survey.Trim()).Append('\n');

        string layoutRef = string.Empty;
        if (f.IsMap && UseScale)
        {
            sb.Append("layout _tp_quickexport\n  scale 1 ").Append(Scale).Append("\nendlayout\n");
            layoutRef = " -layout _tp_quickexport";
        }

        if (f.IsMap)
            sb.Append($"export map -projection {SelectedProjection} -fmt {f.Fmt}{layoutRef} -o \"{OutputFileName}\"");
        else
            sb.Append($"export model -fmt {f.Fmt} -o \"{OutputFileName}\"");

        return sb.ToString();
    }
}
