// "Find All References" results (#7). The search itself lives in Therion.Semantics.SymbolReferences —
// the same symbol resolution rename uses — so this window only presents spans and navigates to them.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Therion.Core;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.Views;

/// <summary>One row of the results grid.</summary>
public sealed class ReferenceRow
{
    public required string FileName { get; init; }
    public required int Line { get; init; }
    public required string Kind { get; init; }
    public required string Preview { get; init; }
    public required SourceSpan Span { get; init; }
}

public partial class ReferencesWindow : Window
{
    private readonly IDocumentService? _docs;

    public ReferencesWindow() : this(string.Empty, Array.Empty<Therion.Semantics.SymbolReference>(), null) { }

    public ReferencesWindow(string symbolName,
        IReadOnlyList<Therion.Semantics.SymbolReference> references, IDocumentService? docs)
    {
        AvaloniaXamlLoader.Load(this);
        _docs = docs;

        var rows = references.Select(Row).ToList();
        this.FindControl<DataGrid>("Grid")!.ItemsSource = rows;
        this.FindControl<TextBlock>("Header")!.Text =
            string.Format(Tr.Get("Refs_Header"), symbolName, rows.Count,
                rows.Select(r => r.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private ReferenceRow Row(Therion.Semantics.SymbolReference r) => new()
    {
        FileName = Path.GetFileName(r.Span.FilePath),
        Line = r.Span.Start.Line,
        Kind = Tr.Get(r.IsDeclaration ? "Refs_Kind_Declaration" : "Refs_Kind_Reference"),
        Preview = LineTextAt(r.Span),
        Span = r.Span,
    };

    // The source line the span sits on, trimmed. Best-effort: an unreadable file just shows nothing.
    private static string LineTextAt(SourceSpan span)
    {
        try
        {
            var line = span.Start.Line;
            if (line < 1 || !File.Exists(span.FilePath)) return string.Empty;
            using var reader = new StreamReader(span.FilePath);
            for (var i = 1; i < line; i++)
                if (reader.ReadLine() is null) return string.Empty;
            return reader.ReadLine()?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => NavigateToSelection();
    private void OnNavigateClick(object? sender, RoutedEventArgs e) => NavigateToSelection();
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void NavigateToSelection()
    {
        if (this.FindControl<DataGrid>("Grid")?.SelectedItem is not ReferenceRow row) return;
        _ = _docs?.NavigateToSpanAsync(row.Span);
    }
}
