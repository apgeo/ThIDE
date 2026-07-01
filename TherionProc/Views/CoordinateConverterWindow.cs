// UTIL-01 — coordinate converter: WGS84 lat/long ↔ UTM, with one-click "copy as Therion fix line".
// Paste a GPS coordinate, get a `fix` line (+ the `cs` to declare) ready for the centreline.

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Therion.Core;
using TherionProc.Resources;

namespace TherionProc.Views;

public sealed class CoordinateConverterWindow : Window
{
    private readonly TextBox _station = new() { Text = "1", MinWidth = 90 };
    private readonly TextBox _alt = new() { Text = "0", MinWidth = 90 };

    // Lat/Lon → UTM.
    private readonly TextBox _lat = new() { Text = "46.5", MinWidth = 120 };
    private readonly TextBox _lon = new() { Text = "8.0", MinWidth = 120 };
    private readonly TextBlock _utmOut = new() { TextWrapping = TextWrapping.Wrap };

    // UTM → Lat/Lon.
    private readonly TextBox _zone = new() { Text = "32", MinWidth = 70 };
    private readonly ComboBox _hemi = new() { ItemsSource = new[] { "N", "S" }, SelectedIndex = 0, MinWidth = 70 };
    private readonly TextBox _east = new() { Text = "500000", MinWidth = 120 };
    private readonly TextBox _north = new() { Text = "5150000", MinWidth = 120 };
    private readonly TextBlock _llOut = new() { TextWrapping = TextWrapping.Wrap };

    private string _utmFix = string.Empty, _llFix = string.Empty;

    public CoordinateConverterWindow()
    {
        Title = Tr.Get("CC_Title");
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        foreach (var tb in new[] { _station, _alt, _lat, _lon, _zone, _east, _north })
            tb.TextChanged += (_, _) => Recompute();
        _hemi.SelectionChanged += (_, _) => Recompute();

        var copyUtm = new Button { Content = Tr.Get("CC_CopyUtm") };
        copyUtm.Click += (_, _) => Services.ClipboardHelper.SetText(_utmFix);
        var copyLl = new Button { Content = Tr.Get("CC_CopyLl") };
        copyLl.Click += (_, _) => Services.ClipboardHelper.SetText(_llFix);
        var close = new Button { Content = Tr.Get("Common_Close"), IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();

        var common = Row(Tr.Get("Col_Station"), _station, Tr.Get("CC_Altitude"), _alt);

        var toUtm = new StackPanel { Spacing = 6, Children =
        {
            Header(Tr.Get("CC_ToUtmHeader")),
            Row(Tr.Get("CC_Latitude"), _lat, Tr.Get("CC_Longitude"), _lon),
            _utmOut,
            Left(copyUtm),
        } };

        var toLl = new StackPanel { Spacing = 6, Margin = new(0, 10, 0, 0), Children =
        {
            Header(Tr.Get("CC_ToLlHeader")),
            Row(Tr.Get("CC_Zone"), _zone, Tr.Get("CC_Hemisphere"), _hemi),
            Row(Tr.Get("CC_Easting"), _east, Tr.Get("CC_Northing"), _north),
            _llOut,
            Left(copyLl),
        } };

        Content = new StackPanel
        {
            Margin = new(16),
            Spacing = 8,
            Children =
            {
                common, toUtm, toLl,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new(0, 6, 0, 0), Children = { close } },
            },
        };

        Recompute();
    }

    private void Recompute()
    {
        var station = string.IsNullOrWhiteSpace(_station.Text) ? "1" : _station.Text!.Trim();
        var alt = _alt.Text ?? "0";

        // Lat/Lon → UTM.
        if (TryD(_lat.Text, out var lat) && TryD(_lon.Text, out var lon))
        {
            var u = CoordinateConverter.LatLonToUtm(lat, lon);
            _utmOut.Text = $"Zone {u.ZoneLabel} · E {u.Easting:0.##} · N {u.Northing:0.##}   (cs UTM{u.ZoneLabel})";
            _utmFix = $"cs UTM{u.ZoneLabel}\nfix {station} {u.Easting:0.##} {u.Northing:0.##} {alt}";
        }
        else { _utmOut.Text = "—"; _utmFix = string.Empty; }

        // UTM → Lat/Lon.
        if (int.TryParse(_zone.Text, out var zone) && zone is >= 1 and <= 60 &&
            TryD(_east.Text, out var e) && TryD(_north.Text, out var n))
        {
            var (la, lo) = CoordinateConverter.UtmToLatLon(new UtmCoordinate(zone, _hemi.SelectedIndex == 0, e, n));
            _llOut.Text = $"Lat {la:0.0000000} · Lon {lo:0.0000000}   (cs lat-long → fix is lon lat alt)";
            _llFix = $"cs lat-long\nfix {station} {lo:0.0000000} {la:0.0000000} {alt}";
        }
        else { _llOut.Text = "—"; _llFix = string.Empty; }
    }

    private static bool TryD(string? s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.Bold };

    private static Control Left(Control c) =>
        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Children = { c } };

    private static Control Row(string l1, Control c1, string l2, Control c2) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 6, Children =
        {
            new TextBlock { Text = l1, VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 }, c1,
            new TextBlock { Text = l2, VerticalAlignment = VerticalAlignment.Center, MinWidth = 90, Margin = new(10, 0, 0, 0) }, c2,
        },
    };
}
