// magnetic declination calculator. Computes declination for a lat/long + date using a
// World Magnetic Model (WMM.COF). The model file is user-supplied (public-domain NOAA download) and
// looked up in %AppData%/ThIDE or next to the app; a "Load model…" button picks one manually.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Therion.Core;
using ThIDE.Resources;

namespace ThIDE.Views;

public sealed class DeclinationWindow : Window
{
    private readonly TextBox _lat = new() { Text = "46.5", MinWidth = 120 };
    private readonly TextBox _lon = new() { Text = "8.0", MinWidth = 120 };
    private readonly TextBox _year = new() { MinWidth = 120 };
    private readonly TextBlock _model = new() { Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _result = new() { FontSize = 18, FontWeight = FontWeight.Bold };
    private readonly Button _copy = new() { Content = Tr.Get("DC_CopyLine"), IsEnabled = false };

    private GeoMagneticModel? _geoModel;
    private string _declLine = string.Empty;

    public DeclinationWindow()
    {
        Title = Tr.Get("DC_Title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _year.Text = CurrentDecimalYear().ToString("0.0", CultureInfo.InvariantCulture);
        _geoModel = TryLoadDefaultModel();

        foreach (var tb in new[] { _lat, _lon, _year }) tb.TextChanged += (_, _) => Recompute();

        var load = new Button { Content = Tr.Get("DC_LoadModel") };
        load.Click += async (_, _) => await PickModelAsync();
        _copy.Click += (_, _) => Services.ClipboardHelper.SetText(_declLine);
        var close = new Button { Content = Tr.Get("Common_Close"), IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new(16),
            Spacing = 8,
            Children =
            {
                Row(Tr.Get("CC_Latitude"), _lat),
                Row(Tr.Get("CC_Longitude"), _lon),
                Row(Tr.Get("DC_Year"), _year),
                _result,
                _model,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 6, 0, 0),
                    Children = { load, _copy } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close } },
            },
        };

        Recompute();
    }

    private void Recompute()
    {
        if (_geoModel is null)
        {
            _result.Text = Tr.Get("DC_NoModel");
            _model.Text = Tr.Get("DC_NoModelHelp");
            _copy.IsEnabled = false;
            return;
        }

        _model.Text = $"Model: {_geoModel.Name} (epoch {_geoModel.Epoch:0.0})";
        if (!TryD(_lat.Text, out var lat) || !TryD(_lon.Text, out var lon) || !TryD(_year.Text, out var year))
        {
            _result.Text = "—"; _copy.IsEnabled = false; return;
        }

        try
        {
            double d = _geoModel.Declination(lat, lon, 0, year);
            var hemi = d >= 0 ? "E" : "W";
            _result.Text = string.Format(Tr.Get("DC_DeclinationFmt"), d.ToString("0.00"), hemi);
            _declLine = $"declination {d:0.00} degrees";
            _copy.IsEnabled = true;
        }
        catch
        {
            _result.Text = "—"; _copy.IsEnabled = false;
        }
    }

    private async System.Threading.Tasks.Task PickModelAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.Get("DC_PickTitle"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Tr.Get("DC_PickFilter")) { Patterns = new[] { "*.cof", "*.COF" } } },
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        try { _geoModel = GeoMagneticModel.FromCof(File.ReadAllText(path)); }
        catch (Exception ex) { _result.Text = Tr.Get("DC_LoadFailed"); _model.Text = ex.Message; _copy.IsEnabled = false; return; }
        Recompute();
    }

    private static GeoMagneticModel? TryLoadDefaultModel()
    {
        foreach (var path in CandidatePaths())
        {
            try { if (File.Exists(path)) return GeoMagneticModel.FromCof(File.ReadAllText(path)); }
            catch { /* try the next candidate */ }
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<string> CandidatePaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData)) yield return Path.Combine(appData, "ThIDE", "WMM.COF");
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "WMM.COF");
        yield return Path.Combine(baseDir, "Assets", "WMM.COF");
    }

    private static double CurrentDecimalYear()
    {
        var now = DateTime.UtcNow;
        double dayFrac = (now.DayOfYear - 1) / 365.0;
        return now.Year + dayFrac;
    }

    private static bool TryD(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static Control Row(string label, Control c) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 8,
        Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, MinWidth = 80 }, c },
    };
}
