using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class SearchWindow : Window
{
    public SearchWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            (DataContext as SearchViewModel)?.PrepareDefaults();
            this.FindControl<TextBox>("QueryBox")?.Focus();
        };
    }

    // Single-click: load the file into the inline peek and scroll to the match (#5).
    private void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SearchViewModel vm || vm.Selected is not { } hit) return;
        if (this.FindControl<TextEditor>("Peek") is not { } peek) return;
        try
        {
            peek.Text = File.Exists(hit.Span.FilePath) ? File.ReadAllText(hit.Span.FilePath) : string.Empty;
            int line = Math.Max(1, hit.Span.Start.Line);
            if (line <= peek.Document.LineCount)
            {
                peek.ScrollToLine(line);
                peek.CaretOffset = peek.Document.GetOffset(line, Math.Max(1, hit.Span.Start.Column));
            }
        }
        catch { peek.Text = string.Empty; }
    }

    // Double-click: navigate to the match in the main editor.
    private void OnResultActivated(object? sender, TappedEventArgs e)
    {
        if (DataContext is SearchViewModel vm && vm.Selected is { } hit)
            vm.ActivateCommand.Execute(hit);
    }
}
