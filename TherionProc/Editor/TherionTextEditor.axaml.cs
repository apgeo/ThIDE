// Implementation Plan �7.3 � AvaloniaEdit-hosted Therion editor surface.
// Exposes a Text property and wires the TherionColorizer for live syntax highlighting.
// F12 / Ctrl+Click ? Go to Definition via an injectable ISymbolNavigationService.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
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

    public static readonly StyledProperty<IEnumerable<string>?> CompletionTermsProperty =
        AvaloniaProperty.Register<TherionTextEditor, IEnumerable<string>?>(nameof(CompletionTerms));

    /// <summary>Raised when the user Ctrl+Clicks / F12s an <c>input</c>/<c>load</c> line; carries the resolved file path.</summary>
    public event EventHandler<string>? OpenFileRequested;

    private TextEditor? _editor;
    private DiagnosticSquiggleRenderer? _squiggles;
    private CompletionWindow? _completionWindow;

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
            _editor.TextArea.TextEntered += OnTextEntered;
            _editor.TextChanged += OnEditorTextChanged;
        }
    }

    // Push user edits back to the bindable Text property so a TwoWay binding
    // flushes them to the document model (which re-parses).
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        SetCurrentValue(TextProperty, _editor.Text);
    }

    /// <summary>Autocomplete vocabulary (Therion keywords + station/survey names).</summary>
    public IEnumerable<string>? CompletionTerms
    {
        get => GetValue(CompletionTermsProperty);
        set => SetValue(CompletionTermsProperty, value);
    }

    /// <summary>Optional diagnostic list � drives squiggle adornments.</summary>
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
        {
            // Only overwrite when the value genuinely differs (e.g. a new document
            // or external edit) — otherwise echoing the user's own keystrokes back
            // would reset the caret to the start of the document.
            var v = change.GetNewValue<string?>() ?? string.Empty;
            if (_editor.Text != v) _editor.Text = v;
        }
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
        else if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            ShowCompletion(explicitTrigger: true);
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        // Auto-suggest as soon as a word is being typed; the window filters live.
        if (e.Text is { Length: > 0 } t && (char.IsLetter(t[0]) || t[0] == '-'))
            ShowCompletion(explicitTrigger: false);
    }

    private void ShowCompletion(bool explicitTrigger)
    {
        if (_editor is null || _completionWindow is not null) return;
        if (CompletionTerms is not { } terms) return;

        int caret = _editor.CaretOffset;
        int start = caret;
        var text = _editor.Text;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var prefix = text.Substring(start, caret - start);

        // Don't pop up unprompted with nothing typed.
        if (!explicitTrigger && prefix.Length == 0) return;

        var matches = terms
            .Where(term => prefix.Length == 0 || term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(term => term, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
        if (matches.Count == 0) return;

        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = start,
            EndOffset = caret,
        };
        foreach (var m in matches)
            window.CompletionList.CompletionData.Add(new TherionCompletionData(m));
        window.Closed += (_, _) => _completionWindow = null;
        _completionWindow = window;
        window.Show();
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
        if (_editor is null) return;
        // Linked-file navigation takes priority over symbol go-to-definition.
        if (TryNavigateLinkedFile(offset)) return;
        if (Navigation is null) return;
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
        int start = offset;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsWordChar(text[end])) end++;
        return text.Substring(start, end - start);
    }

    // Therion station refs are letters, digits, '.', '_', '-'.
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';

    // ----- linked-file navigation (input / load) --------------------------

    private static readonly Regex InputLine =
        new(@"^\s*(?:input|load)\s+(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private bool TryNavigateLinkedFile(int offset)
    {
        if (_editor is null) return false;
        var line = _editor.Document.GetLineByOffset(offset);
        var lineText = _editor.Document.GetText(line);
        var m = InputLine.Match(lineText);
        if (!m.Success) return false;

        var raw = m.Groups[1].Value.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"') raw = raw[1..^1];

        var resolved = ResolveInputPath(raw);
        if (resolved is null) return false;
        OpenFileRequested?.Invoke(this, resolved);
        return true;
    }

    private string? ResolveInputPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath)) return null;
        var baseDir = Path.GetDirectoryName(CurrentFilePath);
        if (string.IsNullOrEmpty(baseDir)) return null;

        var rel = raw.Replace('/', Path.DirectorySeparatorChar);
        string full;
        try { full = Path.GetFullPath(Path.Combine(baseDir, rel)); }
        catch { return null; }

        if (File.Exists(full)) return full;
        // Therion input paths often omit the .th extension.
        if (!Path.HasExtension(full) && File.Exists(full + ".th")) return full + ".th";
        return null;
    }

    private sealed class TherionCompletionData : ICompletionData
    {
        public TherionCompletionData(string text) => Text = text;

        public IImage? Image => null;
        public string Text { get; }
        public object Content => Text;
        public object Description => "Therion term";
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
            => textArea.Document.Replace(completionSegment, Text);
    }
}
