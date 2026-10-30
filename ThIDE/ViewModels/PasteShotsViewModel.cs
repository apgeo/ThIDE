using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Import;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

/// <summary>
/// Edit ▸ Paste as shot data: a table of shots (spreadsheet, CSV, PocketTopo or DistoX text) shown as
/// the Therion <c>data</c> block it becomes, with the guesses (columns, units) open to correction.
/// </summary>
public sealed partial class PasteShotsViewModel : ObservableObject
{
    public PasteShotsViewModel(string source, string? startStation, bool wrap)
    {
        _source = source;
        _startStation = string.IsNullOrWhiteSpace(startStation) ? "0" : startStation;
        _wrap = wrap;
        Recompute();
    }

    [ObservableProperty] private string _source;
    /// <summary>Column keywords ("from to length compass clino"); empty = from the header or the column count.</summary>
    [ObservableProperty] private string _columns = "";
    [ObservableProperty] private string _startStation;
    /// <summary>0 = from the header, 1 = meters, 2 = feet.</summary>
    [ObservableProperty] private int _lengthUnit;
    /// <summary>0 = inferred, 1 = degrees, 2 = grads.</summary>
    [ObservableProperty] private int _angleUnit;
    /// <summary>Repeated shots that make one leg in a table without stations (0 = off).</summary>
    [ObservableProperty] private decimal _mergeRepeats = 3;
    [ObservableProperty] private bool _wrap;

    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _detectedColumns = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _canInsert;
    /// <summary>The station fields matter only when the table has no station columns.</summary>
    [ObservableProperty] private bool _needsStations;

    public ObservableCollection<string> Issues { get; } = new();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Source) or nameof(Columns) or nameof(StartStation) or nameof(LengthUnit)
            or nameof(AngleUnit) or nameof(MergeRepeats) or nameof(Wrap))
            Recompute();
    }

    private void Recompute()
    {
        Issues.Clear();
        var columns = string.IsNullOrWhiteSpace(Columns) ? null : ShotTableReader.ParseColumns(Columns);
        if (!string.IsNullOrWhiteSpace(Columns) && columns is null) Issues.Add(Tr.Get("PasteShots_BadColumns"));
        var options = new ShotTableOptions(
            columns,
            StartStation,
            LengthUnit switch { 1 => ShotLengthUnit.Meters, 2 => ShotLengthUnit.Feet, _ => null },
            AngleUnit switch { 1 => ShotAngleUnit.Degrees, 2 => ShotAngleUnit.Grads, _ => null },
            (int)MergeRepeats);
        var table = ShotTableReader.Read(Source ?? "", options);
        foreach (var issue in table.Issues) Issues.Add(Describe(issue));
        NeedsStations = !table.HasStations;
        DetectedColumns = string.Join(' ', table.Columns.Select(ShotTableReader.Keyword));
        CanInsert = table.Rows.Count > 0;
        Output = CanInsert ? ShotTableReader.ToTherion(table, "  ", Wrap) : "";
        Summary = string.Format(Tr.Get("PasteShots_Summary"), table.Rows.Count(r => !r.Splay), table.Rows.Count(r => r.Splay));
    }

    public static string Describe(ShotIssue issue)
    {
        var text = string.Format(Tr.Get("ShotIssue_" + issue.Kind), issue.Detail);
        return issue.Line > 0 ? string.Format(Tr.Get("ShotIssue_Line"), issue.Line, text) : text;
    }
}
