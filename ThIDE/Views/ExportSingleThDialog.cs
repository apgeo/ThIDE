// Modal dialog (code-only, no .axaml) collecting the options of Tools ▸ Export as a single .th file:
// the whole project merged into one survey with one centreline. Returns the options, or null when
// cancelled; the file is picked afterwards by the caller.

using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Therion.Workspace.Flatten;
using ThIDE.Resources;

namespace ThIDE.Views;

/// <summary>The choices of the dialog: the export's options, and whether to save the list of station names too.</summary>
public sealed record ExportSingleThChoices(FlattenOptions Options, bool WriteStationNames);

public sealed class ExportSingleThDialog : Window
{
    public ExportSingleThDialog()
    {
        Title = Tr.Get("Flatten_Title");
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var survey = new TextBox { Watermark = Tr.Get("Flatten_SurveyHint") };
        survey.AttachedToVisualTree += (_, _) => survey.Focus();
        var naming = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { Tr.Get("Flatten_NamingKeep"), Tr.Get("Flatten_NamingSurvey"), Tr.Get("Flatten_NamingSequential") },
            SelectedIndex = 0,
        };
        var prefix = new TextBox { Watermark = Tr.Get("Flatten_PrefixHint") };
        var splays = new TextBox { Text = "100", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
        var limit = Check(Tr.Get("Flatten_LimitSplays"), false);
        var maxSplays = new TextBox { Text = "3", Width = 80, IsEnabled = false };
        limit.IsCheckedChanged += (_, _) => maxSplays.IsEnabled = limit.IsChecked == true;
        var standalone = Check(Tr.Get("Flatten_Standalone"), true);
        var declination = Check(Tr.Get("Flatten_Declination"), true);
        var latitude = new TextBox { Watermark = Tr.Get("Flatten_Latitude"), Width = 120 };
        var longitude = new TextBox { Watermark = Tr.Get("Flatten_Longitude"), Width = 120 };
        declination.IsCheckedChanged += (_, _) => latitude.IsEnabled = longitude.IsEnabled = declination.IsChecked == true;
        var comments = Check(Tr.Get("Flatten_SourceComments"), true);
        var names = Check(Tr.Get("Flatten_WriteNames"), false);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };

        var ok = new Button { Content = Tr.Get("Flatten_ChooseFile"), IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = Tr.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) =>
        {
            if (!double.TryParse(splays.Text?.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var pct)
                && !double.TryParse(splays.Text?.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out pct)
                || pct is < 0 or > 100)
            {
                Show(error, Tr.Get("Flatten_BadPercent"));
                return;
            }
            int? max = null;
            if (limit.IsChecked == true)
            {
                if (!int.TryParse(maxSplays.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var m))
                {
                    Show(error, Tr.Get("Flatten_BadMax"));
                    return;
                }
                max = m;
            }
            (double, double)? location = null;
            bool hasLat = !string.IsNullOrWhiteSpace(latitude.Text), hasLon = !string.IsNullOrWhiteSpace(longitude.Text);
            if (declination.IsChecked == true && (hasLat || hasLon))
            {
                if (!Degrees(latitude.Text, 90, out var lat) || !Degrees(longitude.Text, 180, out var lon))
                {
                    Show(error, Tr.Get("Flatten_BadLocation"));
                    return;
                }
                location = (lat, lon);
            }
            var surveyName = survey.Text?.Trim();
            Close(new ExportSingleThChoices(new FlattenOptions
            {
                SurveyName = string.IsNullOrEmpty(surveyName) ? null : surveyName,
                Naming = naming.SelectedIndex switch
                {
                    1 => FlattenStationNaming.SurveyPrefix,
                    2 => FlattenStationNaming.Sequential,
                    _ => FlattenStationNaming.Keep,
                },
                Prefix = prefix.Text?.Trim() ?? "",
                SplayKeepPercent = pct,
                MaxSplaysPerStation = max,
                Standalone = standalone.IsChecked == true,
                ApplyDeclination = declination.IsChecked == true,
                DeclinationLocation = location,
                SourceComments = comments.IsChecked == true,
                GeneratedAt = System.DateTimeOffset.Now,
            }, names.IsChecked == true));
        };
        cancel.Click += (_, _) => Close(null);

        AutomationProperties.SetAutomationId(this, "ExportSingleTh.Window");
        Id(survey, "ExportSingleTh.Survey", Tr.Get("Flatten_Survey"));
        Id(naming, "ExportSingleTh.Naming", Tr.Get("Flatten_Naming"));
        Id(prefix, "ExportSingleTh.Prefix", Tr.Get("Flatten_Prefix"));
        Id(splays, "ExportSingleTh.SplayPercent", Tr.Get("Flatten_SplayPercent"));
        Id(limit, "ExportSingleTh.LimitSplays", Tr.Get("Flatten_LimitSplays"));
        Id(maxSplays, "ExportSingleTh.MaxSplays", Tr.Get("Flatten_LimitSplays"));
        Id(standalone, "ExportSingleTh.Standalone", Tr.Get("Flatten_Standalone"));
        Id(declination, "ExportSingleTh.Declination", Tr.Get("Flatten_Declination"));
        Id(latitude, "ExportSingleTh.Latitude", Tr.Get("Flatten_Latitude"));
        Id(longitude, "ExportSingleTh.Longitude", Tr.Get("Flatten_Longitude"));
        Id(comments, "ExportSingleTh.SourceComments", Tr.Get("Flatten_SourceComments"));
        Id(names, "ExportSingleTh.WriteNames", Tr.Get("Flatten_WriteNames"));
        Id(ok, "ExportSingleTh.Ok", Tr.Get("Flatten_ChooseFile"));
        Id(cancel, "ExportSingleTh.Cancel", Tr.Get("Common_Cancel"));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 8,
                Children =
                {
                    Hint(Tr.Get("Flatten_Intro")),
                    new TextBlock { Text = Tr.Get("Flatten_Survey") },
                    survey,
                    new TextBlock { Text = Tr.Get("Flatten_Naming") },
                    naming,
                    new TextBlock { Text = Tr.Get("Flatten_Prefix") },
                    prefix,
                    new TextBlock { Text = Tr.Get("Flatten_SplayPercent") },
                    splays,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { limit, maxSplays } },
                    Hint(Tr.Get("Flatten_SplayHint")),
                    standalone,
                    declination,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { latitude, longitude } },
                    Hint(Tr.Get("Flatten_LocationHint")),
                    comments,
                    names,
                    error,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 6, 0, 0),
                        Children = { cancel, ok },
                    },
                },
            },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    }

    public Task<ExportSingleThChoices?> ShowAsync(Window owner) => ShowDialog<ExportSingleThChoices?>(owner);

    private static CheckBox Check(string text, bool on) =>
        new() { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on };

    private static TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };

    private static void Id(Control control, string id, string name)
    {
        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, name);
    }

    private static void Show(TextBlock error, string text)
    {
        error.Text = text;
        error.IsVisible = true;
    }

    private static bool Degrees(string? text, double limit, out double value)
    {
        var t = text?.Trim() ?? "";
        bool ok = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                  || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        return ok && value >= -limit && value <= limit;
    }
}
