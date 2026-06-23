using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using TherionProc.ViewModels;

namespace TherionProc.Views;

public partial class ReplaceWindow : Window
{
    public ReplaceWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            (DataContext as ReplaceInFilesViewModel)?.PrepareDefaults();
            this.FindControl<TextBox>("QueryBox")?.Focus();
        };
        // Esc closes the window (mirrors Find in Files, #8/#9). Tunnel so it fires even while a
        // TextBox holds focus.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }

    // Single-click: load the file into the inline peek and scroll to the match.
    private void OnResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ReplaceInFilesViewModel vm || vm.Selected is not { } hit) return;
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
        if (DataContext is ReplaceInFilesViewModel vm && vm.Selected is { } hit)
            vm.ActivateCommand.Execute(hit);
    }
}
