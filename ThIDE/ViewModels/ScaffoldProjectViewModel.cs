// Options dialog for "Scaffold Therion project from survey". Collects the project name, optional
// georeferencing, layout and the set of export targets, then hands a ScaffoldOptions to the pure
// TopodroidProjectScaffold. All string building lives in the core lib; this is presentation only.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Therion.Workspace.Import;

namespace ThIDE.ViewModels;

/// <summary>A toggleable export target shown as a checkbox in the dialog.</summary>
public sealed partial class ExportChoice : ObservableObject
{
    public string Display { get; }
    public ExportItem Item { get; }
    [ObservableProperty] private bool _isSelected;

    public ExportChoice(string display, ExportItem item, bool selected)
    {
        Display = display;
        Item = item;
        _isSelected = selected;
    }
}

public sealed partial class ScaffoldProjectViewModel : ObservableObject
{
    [ObservableProperty] private string _projectName;
    [ObservableProperty] private string _innerSurveyName;
    [ObservableProperty] private string _sourceFileName;
    [ObservableProperty] private string _title;

    // georeferencing (optional)
    [ObservableProperty] private string _entranceStation;
    [ObservableProperty] private string _coordinateSystem = "lat-long";
    [ObservableProperty] private string _fixCoord1 = string.Empty;   // in CS order (lat for lat-long)
    [ObservableProperty] private string _fixCoord2 = string.Empty;   // long for lat-long
    [ObservableProperty] private string _fixAltitude = "0";

    // layout + tree
    [ObservableProperty] private bool _includeLayout = true;
    [ObservableProperty] private int _scale = 500;
    [ObservableProperty] private bool _legend = true;
    [ObservableProperty] private bool _createGraphicsDir;

    public IReadOnlyList<string> CoordinateSystems { get; } = new[]
    {
        "lat-long", "long-lat", "EPSG:4326",
        "UTM33N", "UTM34N", "UTM35N",
        "S-MERC", "iJTSK", "iJTSK03",
    };

    public ObservableCollection<ExportChoice> Exports { get; }

    /// <summary>Designer / fallback ctor.</summary>
    public ScaffoldProjectViewModel()
        : this("cave", new SourceSurveyInfo("cave", string.Empty, string.Empty), "cave.th") { }

    public ScaffoldProjectViewModel(string projectName, SourceSurveyInfo info, string sourceFileName)
    {
        _projectName = projectName;
        _innerSurveyName = info.SurveyName;
        _sourceFileName = sourceFileName;
        _title = info.Title;
        _entranceStation = info.EntranceHint;
        Exports = new ObservableCollection<ExportChoice>(DefaultExports());
    }

    /// <summary>Builds the VM's initial state by sniffing a TopoDroid survey file's text + path.</summary>
    public static ScaffoldProjectViewModel FromSource(string sourcePath, string sourceText)
    {
        var info = TopodroidProjectScaffold.Parse(sourceText);
        var fileName = Path.GetFileName(sourcePath);
        var project = DeriveProjectName(Path.GetFileNameWithoutExtension(sourcePath), info.SurveyName);
        return new ScaffoldProjectViewModel(project, info, fileName);
    }

    /// <summary>Prefer the survey name with a trailing year stripped; fall back to the file name.</summary>
    private static string DeriveProjectName(string fileBase, string surveyName)
    {
        var name = string.IsNullOrWhiteSpace(surveyName) ? fileBase : surveyName;
        // drop a trailing _YYYY (TopoDroid often names surveys "<cave>_2025")
        var trimmed = System.Text.RegularExpressions.Regex.Replace(name, @"_\d{4}$", string.Empty);
        return string.IsNullOrWhiteSpace(trimmed) ? "cave" : trimmed;
    }

    private static IEnumerable<ExportChoice> DefaultExports() => new[]
    {
        // 3D model — extension drives the default output name; empty projection = not a map.
        new ExportChoice("Loch 3D model (.lox)",   new ExportItem(ExportKind.Model, "loch",    ".lox"), true),
        new ExportChoice("Survex 3D (.3d)",        new ExportItem(ExportKind.Model, "survex",  ".3d"),  true),
        new ExportChoice("KML (Google Earth)",     new ExportItem(ExportKind.Model, "kml",     ".kml"), true),
        new ExportChoice("ESRI shapefile (.shp)",  new ExportItem(ExportKind.Model, "shp",     ".shp", WallSource: "splays"), true),
        new ExportChoice("Compass (.plt)",         new ExportItem(ExportKind.Model, "compass", ".plt"), false),
        new ExportChoice("VRML (.wrl)",            new ExportItem(ExportKind.Model, "vrml",    ".wrl"), false),
        new ExportChoice("DXF (.dxf)",             new ExportItem(ExportKind.Model, "dxf",     ".dxf"), false),
        new ExportChoice("3DMF (.3dmf)",           new ExportItem(ExportKind.Model, "3dmf",    ".3dmf"), false),

        // 2D map — projection distinguishes output names and is emitted as -projection.
        new ExportChoice("PDF map — plan",              new ExportItem(ExportKind.Map, "pdf",  ".pdf",   Projection: "plan",     UseLayout: true), true),
        new ExportChoice("PDF map — extended elevation",new ExportItem(ExportKind.Map, "pdf",  ".pdf",   Projection: "extended", UseLayout: true), false),
        new ExportChoice("SVG map — plan",              new ExportItem(ExportKind.Map, "svg",  ".svg",   Projection: "plan",     UseLayout: true), true),
        new ExportChoice("XHTML map — plan",            new ExportItem(ExportKind.Map, "xhtml",".xhtml", Projection: "plan",     UseLayout: true), false),

        // database + lists
        new ExportChoice("SQL database (.sql)",   new ExportItem(ExportKind.Database, "sql",  ".sql"), true),
        new ExportChoice("CSV database (.csv)",   new ExportItem(ExportKind.Database, "csv",  ".csv"), false),
        new ExportChoice("Survey list (HTML)",    new ExportItem(ExportKind.SurveyList, "html", "-surveys.html"), false),
    };

    /// <summary>Snapshots the dialog state into the immutable options record consumed by the scaffolder.</summary>
    public ScaffoldOptions BuildOptions() => new()
    {
        ProjectName = string.IsNullOrWhiteSpace(ProjectName) ? "cave" : ProjectName.Trim(),
        InnerSurveyName = InnerSurveyName.Trim(),
        SourceFileName = SourceFileName.Trim(),
        Title = Title.Trim(),
        EntranceStation = EntranceStation.Trim(),
        CoordinateSystem = string.IsNullOrWhiteSpace(CoordinateSystem) ? "lat-long" : CoordinateSystem.Trim(),
        FixC1 = FixCoord1.Trim(),
        FixC2 = FixCoord2.Trim(),
        FixC3 = string.IsNullOrWhiteSpace(FixAltitude) ? "0" : FixAltitude.Trim(),
        IncludeLayout = IncludeLayout,
        Scale = Scale,
        Legend = Legend,
        CreateGraphicsDir = CreateGraphicsDir,
        Exports = Exports.Where(e => e.IsSelected).Select(e => e.Item).ToList(),
    };
}
