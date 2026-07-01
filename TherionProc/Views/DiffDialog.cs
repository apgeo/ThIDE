// a read-only side-by-side comparison (e.g. on-disk vs in-editor) so the user can see
// what an external change did before deciding to reload, keep, or overwrite. Not a 3-way merge —
// it's a quick visual diff with changed lines highlighted.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TherionProc.Views;

public sealed class DiffDialog : Window
{
    public DiffDialog(string title, string leftLabel, string leftText, string rightLabel, string rightText)
    {
        Title = title;
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,*") };

        var l = Header(leftLabel); Grid.SetRow(l, 0); Grid.SetColumn(l, 0); grid.Children.Add(l);
        var r = Header(rightLabel); Grid.SetRow(r, 0); Grid.SetColumn(r, 1); grid.Children.Add(r);

        var changed = ChangedLines(leftText, rightText);
        var left = Pane(leftText, changed); Grid.SetRow(left, 1); Grid.SetColumn(left, 0); grid.Children.Add(left);
        var right = Pane(rightText, changed); Grid.SetRow(right, 1); Grid.SetColumn(right, 1); grid.Children.Add(right);

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) };
        close.Click += (_, _) => Close();

        Content = new DockPanel { Margin = new(10), Children = { Bottom(close), grid } };
        DockPanel.SetDock(grid, Avalonia.Controls.Dock.Top);
    }

    private static Control Bottom(Control c) { DockPanel.SetDock(c, Avalonia.Controls.Dock.Bottom); return c; }

    private static TextBlock Header(string text) =>
        new() { Text = text, FontWeight = FontWeight.Bold, Margin = new(4, 0, 4, 4) };

    private static Control Pane(string text, HashSet<int> changed)
    {
        var panel = new StackPanel();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            panel.Children.Add(new TextBlock
            {
                Text = lines[i].Length == 0 ? " " : lines[i],
                FontFamily = new FontFamily("Consolas,Menlo,monospace"),
                FontSize = 12,
                Background = changed.Contains(i) ? new SolidColorBrush(Color.FromArgb(0x33, 0xE6, 0x51, 0x00)) : null,
                TextWrapping = TextWrapping.NoWrap,
            });
        }
        return new ScrollViewer { Content = panel, Margin = new(2), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    // Line indices that differ between the two texts (simple positional comparison).
    private static HashSet<int> ChangedLines(string a, string b)
    {
        var la = a.Replace("\r\n", "\n").Split('\n');
        var lb = b.Replace("\r\n", "\n").Split('\n');
        var set = new HashSet<int>();
        int max = Math.Max(la.Length, lb.Length);
        for (int i = 0; i < max; i++)
        {
            var x = i < la.Length ? la[i] : null;
            var y = i < lb.Length ? lb[i] : null;
            if (!string.Equals(x, y, StringComparison.Ordinal)) set.Add(i);
        }
        return set;
    }
}
