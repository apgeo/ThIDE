// PROJ-08 — breadcrumb of the @-qualified name at the caret. Splits the station/survey reference
// under the cursor (e.g. cave.upper.u1 or u1@cave.upper) into clickable components; clicking a
// component navigates to that survey (the prefix) or station (the leaf). Shown in the status bar.

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Therion.Processing.Abstractions;
using TherionProc.Services;

namespace TherionProc.ViewModels;

/// <summary>One clickable component of the caret's qualified name.</summary>
public sealed class BreadcrumbSegment
{
    public string Text { get; init; } = string.Empty;
    /// <summary>The dotted prefix / full reference this component resolves to.</summary>
    public string NavToken { get; init; } = string.Empty;
    public ReferenceKind Kind { get; init; }
    public bool IsLast { get; init; }
}

public sealed partial class BreadcrumbViewModel : ObservableObject
{
    private readonly IDocumentService? _documents;

    public ObservableCollection<BreadcrumbSegment> Segments { get; } = new();
    [ObservableProperty] private bool _hasBreadcrumb;

    public BreadcrumbViewModel() { } // design-time
    public BreadcrumbViewModel(IDocumentService documents) => _documents = documents;

    /// <summary>Rebuilds the breadcrumb from the identifier under <paramref name="offset"/>.</summary>
    public void Update(string? text, int offset)
    {
        Segments.Clear();
        var token = TokenAt(text, offset);
        if (token is null || (!token.Contains('.') && !token.Contains('@')))
        {
            HasBreadcrumb = false;
            return;
        }

        int at = token.IndexOf('@');
        if (at >= 0)
        {
            var point = token[..at];
            var parts = token[(at + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                Add(parts[i], string.Join('.', parts, 0, i + 1), ReferenceKind.Survey, false);
            if (point.Length > 0) Add(point, token, ReferenceKind.Station, true);
        }
        else
        {
            var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                Add(parts[i], string.Join('.', parts, 0, i + 1),
                    last ? ReferenceKind.Any : ReferenceKind.Survey, last);
            }
        }

        HasBreadcrumb = Segments.Count >= 2;
    }

    private void Add(string text, string nav, ReferenceKind kind, bool isLast) =>
        Segments.Add(new BreadcrumbSegment { Text = text, NavToken = nav, Kind = kind, IsLast = isLast });

    [RelayCommand]
    private void Navigate(BreadcrumbSegment? segment)
    {
        if (segment is null || _documents?.Workspace is not { } ws) return;
        if (ws.ResolveReference(segment.NavToken, segment.Kind) is { IsEmpty: false } span)
            _ = _documents.NavigateToSpanAsync(span);
    }

    /// <summary>The maximal station/survey-reference token straddling <paramref name="offset"/>.</summary>
    private static string? TokenAt(string? text, int offset)
    {
        if (string.IsNullOrEmpty(text)) return null;
        offset = Math.Clamp(offset, 0, text.Length);
        int s = offset, e = offset;
        while (s > 0 && IsRefChar(text[s - 1])) s--;
        while (e < text.Length && IsRefChar(text[e])) e++;
        if (e <= s) return null;
        var token = text[s..e].Trim('.', '-', '@');
        return token.Length == 0 ? null : token;
    }

    private static bool IsRefChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '@' or '_' or '-';
}
