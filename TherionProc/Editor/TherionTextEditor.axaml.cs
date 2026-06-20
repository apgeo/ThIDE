// Implementation Plan §7.3 — AvaloniaEdit-hosted Therion editor surface.
// Exposes a Text property and wires the TherionColorizer for live syntax highlighting.
// F12 / Ctrl+Click ? Go to Definition via an injectable ISymbolNavigationService.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using Therion.Core;
using Therion.Processing.Abstractions;

namespace TherionProc.Editor;

public partial class TherionTextEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TherionTextEditor, string?>(nameof(Text));

    public static readonly StyledProperty<ISymbolNavigationService?> NavigationProperty =
        AvaloniaProperty.Register<TherionTextEditor, ISymbolNavigationService?>(nameof(Navigation));

    public static readonly StyledProperty<IReadOnlyList<Diagnostic>?> DiagnosticsProperty =
        AvaloniaProperty.Register<TherionTextEditor, IReadOnlyList<Diagnostic>?>(nameof(Diagnostics));

    public static readonly StyledProperty<string?> CurrentFilePathProperty =
        AvaloniaProperty.Register<TherionTextEditor, string?>(nameof(CurrentFilePath));

    private TextEditor? _editor;
    private DiagnosticSquiggleRenderer? _squiggles;

    public TherionTextEditor()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("Editor");
        if (_editor is not null)
        {
            _editor.TextArea.TextView.LineTransformers.Add(new TherionColorizer());
            _squiggles = new DiagnosticSquiggleRenderer(_editor.TextArea.TextView);
            _editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
            _editor.KeyDown += OnEditorKeyDown;
            _editor.PointerPressed += OnEditorPointerPressed;
        }
    }

    /// <summary>Optional diagnostic list — drives squiggle adornments.</summary>
    public IReadOnlyList<Diagnostic>? Diagnostics
    {
        get => GetValue(DiagnosticsProperty);
        set => SetValue(DiagnosticsProperty, value);
    }

    /// <summary>Currently-loaded file path; used to filter diagnostics by file.</summary>
    public string? CurrentFilePath
    {
        get => GetValue(CurrentFilePathProperty);
        set => SetValue(CurrentFilePathProperty, value);
    }

    /// <summary>Scrolls the caret to a 1-based line/column and focuses the editor.</summary>
    public void ScrollTo(int line, int column)
    {
        if (_editor is null) return;
        if (line < 1 || line > _editor.Document.LineCount) return;
        var col = Math.Max(1, column);
        var offset = _editor.Document.GetOffset(line, col);
        _editor.CaretOffset = offset;
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    /// <summary>Scrolls to the start of a <see cref="SourceSpan"/>.</summary>
    public void ScrollTo(SourceSpan span)
    {
        if (span.IsEmpty) return;
        ScrollTo(span.Start.Line, span.Start.Column);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Optional Go-to-Definition backend (lit up when a workspace snapshot is bound).</summary>
    public ISymbolNavigationService? Navigation
    {
        get => GetValue(NavigationProperty);
        set => SetValue(NavigationProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && _editor is not null)
            _editor.Text = change.GetNewValue<string?>() ?? string.Empty;
        else if (change.Property == DiagnosticsProperty)
            _squiggles?.SetDiagnostics(change.GetNewValue<IReadOnlyList<Diagnostic>?>() ?? Array.Empty<Diagnostic>());
        else if (change.Property == CurrentFilePathProperty)
            _squiggles?.SetFilePath(change.GetNewValue<string?>());
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 && _editor is not null)
        {
            TryNavigateAt(_editor.CaretOffset);
            e.Handled = true;
        }
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null) return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;

        var pos = e.GetPosition(_editor.TextArea.TextView);
        var loc = _editor.TextArea.TextView.GetPosition(pos + _editor.TextArea.TextView.ScrollOffset);
        if (loc is null) return;
        var off = _editor.Document.GetOffset(loc.Value.Location);
        TryNavigateAt(off);
    }

    private void TryNavigateAt(int offset)
    {
        if (_editor is null || Navigation is null) return;
        var word = WordAt(_editor.Text, offset);
        if (string.IsNullOrEmpty(word)) return;
        var span = Navigation.GoToDefinition(word);
        if (span is null) return;

        var line = Math.Max(1, span.Value.Start.Line);
        var col  = Math.Max(1, span.Value.Start.Column);
        if (line > _editor.Document.LineCount) return;
        var docOffset = _editor.Document.GetOffset(line, col);
        _editor.CaretOffset = docOffset;
        _editor.ScrollToLine(line);
        _editor.Focus();
    }

    private static string WordAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return string.Empty;
        // Therion station refs are letters, digits, '.', '_', '-'.
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
        int start = offset;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsWordChar(text[end])) end++;
        return text.Substring(start, end - start);
    }
}
