// a quick unit-converter palette: length (m/ft/cm/km/in/yd) and angle
// (degrees/grads/mils/percent-slope/minutes). Live result; "Copy result" for pasting into data.

using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Therion.Core;
using TherionProc.Resources;

namespace TherionProc.Views;

public sealed class UnitConverterWindow : Window
{
    // Name holds the Strings.resx key; it is resolved to the current language at display time.
    private static readonly (string Name, LengthUnit Unit)[] Lengths =
    {
        ("UC_Metres", LengthUnit.Metre), ("UC_Feet", LengthUnit.Foot), ("UC_Centimetres", LengthUnit.Centimetre),
        ("UC_Kilometres", LengthUnit.Kilometre), ("UC_Inches", LengthUnit.Inch), ("UC_Yards", LengthUnit.Yard),
    };

    private static readonly (string Name, AngleUnit Unit)[] Angles =
    {
        ("UC_Degrees", AngleUnit.Degree), ("UC_Grads", AngleUnit.Grad), ("UC_Mils", AngleUnit.Mil),
        ("UC_PercentSlope", AngleUnit.PercentSlope), ("UC_Minutes", AngleUnit.Minute),
    };

    private readonly ComboBox _category = new() { ItemsSource = new[] { Tr.Get("UC_Length"), Tr.Get("UC_Angle") }, SelectedIndex = 0, MinWidth = 120 };
    private readonly ComboBox _from = new() { MinWidth = 150 };
    private readonly ComboBox _to = new() { MinWidth = 150 };
    private readonly TextBox _input = new() { Text = "1", MinWidth = 120 };
    private readonly TextBlock _result = new() { FontSize = 18, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };

    public UnitConverterWindow()
    {
        Title = Tr.Get("UC_Title");
        Width = 400;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        PopulateUnits();

        _category.SelectionChanged += (_, _) => { PopulateUnits(); Recompute(); };
        _from.SelectionChanged += (_, _) => Recompute();
        _to.SelectionChanged += (_, _) => Recompute();
        _input.TextChanged += (_, _) => Recompute();

        var swap = new Button { Content = "⇄" };
        ToolTip.SetTip(swap, Tr.Get("UC_SwapTip"));
        swap.Click += (_, _) => { int f = _from.SelectedIndex, t = _to.SelectedIndex; _from.SelectedIndex = t; _to.SelectedIndex = f; };

        var copy = new Button { Content = Tr.Get("UC_CopyResult"), MinWidth = 100 };
        copy.Click += (_, _) => Services.ClipboardHelper.SetText(_lastResult);

        var close = new Button { Content = Tr.Get("Common_Close"), IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => Close();

        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
        };
        void Add(Control c, int r, int col, int cs = 1) { Grid.SetRow(c, r); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, cs); grid.Children.Add(c); }
        Add(new TextBlock { Text = Tr.Get("UC_Category"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 6) }, 0, 0);
        Add(_category, 0, 1);
        Add(new TextBlock { Text = Tr.Get("UC_Value"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 6, 8, 6) }, 1, 0);
        Add(_input, 1, 1);
        Add(new TextBlock { Text = Tr.Get("UC_From"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 6, 8, 6) }, 2, 0);
        Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _from, swap } }, 2, 1);
        Add(new TextBlock { Text = Tr.Get("UC_To"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 6, 8, 6) }, 3, 0);
        Add(_to, 3, 1);
        Add(_result, 4, 0, 2);

        Content = new StackPanel
        {
            Children =
            {
                grid,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(16, 0, 16, 16),
                    HorizontalAlignment = HorizontalAlignment.Right, Children = { copy, close },
                },
            },
        };

        Recompute();
    }

    private bool IsLength => _category.SelectedIndex == 0;
    private string _lastResult = string.Empty;

    private void PopulateUnits()
    {
        var names = IsLength ? Lengths.Select(l => Tr.Get(l.Name)).ToArray() : Angles.Select(a => Tr.Get(a.Name)).ToArray();
        _from.ItemsSource = names;
        _to.ItemsSource = names.ToArray();
        _from.SelectedIndex = 0;
        _to.SelectedIndex = 1;
    }

    private void Recompute()
    {
        if (_from.SelectedIndex < 0 || _to.SelectedIndex < 0) return;
        if (!double.TryParse(_input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            _result.Text = "—";
            return;
        }
        double r = IsLength
            ? UnitConverter.Instance.ConvertLength(v, Lengths[_from.SelectedIndex].Unit, Lengths[_to.SelectedIndex].Unit)
            : UnitConverter.Instance.ConvertAngle(v, Angles[_from.SelectedIndex].Unit, Angles[_to.SelectedIndex].Unit);
        _lastResult = double.IsFinite(r) ? r.ToString("0.######", CultureInfo.InvariantCulture) : "∞";
        var toName = IsLength ? Tr.Get(Lengths[_to.SelectedIndex].Name) : Tr.Get(Angles[_to.SelectedIndex].Name);
        _result.Text = $"{_lastResult} {toName.ToLowerInvariant()}";
    }
}
