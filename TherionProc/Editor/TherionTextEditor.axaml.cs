// Implementation Plan §7.3 — AvaloniaEdit-hosted Therion editor surface.
// Beyond syntax highlighting it wires the editor "IDE" features: find/replace,
// code folding, auto-indent, bracket/quote pairing, diagnostic hover tooltips +
// an overview ruler, context-aware autocomplete, and navigation
// (F12 go-to-definition, Shift+F12 find-references, Ctrl+G go-to-line,
// Ctrl+/ toggle comment, Ctrl+click linked-file open).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;
using TherionProc.Services;

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

    /// <summary>Raised when the user activates a file-path hyperlink (input/load/source); carries the resolved file path.</summary>
    public event EventHandler<string>? OpenFileRequested;

    /// <summary>Raised when an export/output file link is activated; it should open in the OS default app (#15).</summary>
    public event EventHandler<string>? OpenExternalRequested;

    /// <summary>Raised when a cross-file reference resolves to a span in a different file.</summary>
    public event EventHandler<SourceSpan>? NavigateToSpanRequested;

    /// <summary>Raised when the caret moves, carrying its document span (for navigation history).</summary>
    public event EventHandler<SourceSpan>? CaretMoved;

    private TextEditor? _editor;
    private TherionColorizer? _colorizer;
    private HyperlinkColorizer? _hyperlinkColorizer;
    private DiagnosticSquiggleRenderer? _squiggles;
    private DiagnosticOverviewRuler? _overviewRuler;
    private FlashHighlightRenderer? _flash;
    private OccurrenceHighlightRenderer? _occurrences;
    private FoldingManager? _foldingManager;
    private CompletionWindow? _completionWindow;
    private Diagnostic? _hoverDiagnostic;
    private IAppSettingsService? _settings;
    private readonly Cursor _handCursor = new(StandardCursorType.Hand);

    private IReadOnlyList<Diagnostic> _boundDiagnostics = Array.Empty<Diagnostic>();
    private List<Diagnostic> _fileWarnings = new();

    // Commands whose arguments include navigable file paths.
    private static readonly HashSet<string> PathCommands = new(StringComparer.OrdinalIgnoreCase)
        { "input", "load", "source", "export", "system" };

    // Commands where a referenced file is expected to exist (so a missing target is
    // a warning). Export/system outputs may not be generated yet, so they're excluded.
    private static readonly HashSet<string> ExpectedFileCommands = new(StringComparer.OrdinalIgnoreCase)
        { "input", "load", "source" };

    private readonly record struct PathLink(
        int Start, int Length, string Raw, string? ResolvedExisting, string AttemptedFull,
        bool Expected, bool ShellOpen);

    // Context-aware completion vocabularies.
    private static readonly string[] FlagValues =
        { "surface", "duplicate", "splay", "approximate", "not", "show", "hide" };
    private static readonly string[] DataStyles =
        { "normal", "diving", "cartesian", "cylpolar", "dimensions", "nosurvey", "topofil" };
    private static readonly string[] DataFields =
    {
        "station", "from", "to", "tape", "length", "compass", "bearing", "clino", "gradient",
        "depth", "fromdepth", "todepth", "up", "down", "left", "right", "ignore", "ignoreall",
        "counter", "fromcount", "tocount", "northing", "easting", "altitude", "newline",
        "backtape", "backcompass", "backbearing", "backclino", "backgradient", "direction", "dip",
    };

    public TherionTextEditor()
    {
        InitializeComponent();
        _editor = this.FindControl<TextEditor>("Editor");
        if (_editor is not null)
        {
            ConfigureEditor(_editor);
            _editor.KeyDown += OnEditorKeyDown;
            // AvaloniaEdit marks PointerPressed handled (caret/selection), so we must
            // opt into handled events to receive the click for hyperlink navigation.
            _editor.AddHandler(InputElement.PointerPressedEvent, OnEditorPointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
            _editor.TextArea.TextView.PointerMoved += OnEditorPointerMoved;
            _editor.TextArea.TextView.PointerExited += OnEditorPointerExited;
            _editor.TextArea.TextEntered += OnTextEntered;
            _editor.TextChanged += OnEditorTextChanged;
            ActualThemeVariantChanged += OnThemeVariantChanged;
        }
    }

    private void ConfigureEditor(TextEditor editor)
    {
        editor.Options.EnableHyperlinks = false;
        ApplyAppSettings(editor);

        // Live-apply preference changes (font size, line numbers, …) to open editors.
        _settings = TryGetSettings();
        if (_settings is not null)
        {
            _settings.Changed += OnAppSettingsChanged;
            DetachedFromVisualTree += (_, _) => _settings.Changed -= OnAppSettingsChanged;
        }

        _colorizer = new TherionColorizer();
        _colorizer.SetVariant(IsDark() ? TherionColorizer.Variant.Dark : TherionColorizer.Variant.Light);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        // Added after the syntax colorizer so the link styling wins on the hovered path.
        _hyperlinkColorizer = new HyperlinkColorizer();
        editor.TextArea.TextView.LineTransformers.Add(_hyperlinkColorizer);

        _squiggles = new DiagnosticSquiggleRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);

        // Dim highlight of every occurrence of the identifier under the caret.
        _occurrences = new OccurrenceHighlightRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_occurrences);
        editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

        // Transient highlight painted on a navigation arrival, then faded out.
        _flash = new FlashHighlightRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_flash);

        editor.TextArea.IndentationStrategy = new TherionIndentationStrategy();
        SearchPanel.Install(editor);                            // Ctrl+F / Ctrl+H
        _foldingManager = FoldingManager.Install(editor.TextArea);
        UpdateFoldings();
        UpdateThconfigDecorations();

        // Inject the overview ruler into column 1 of the layout grid.
        if (this.FindControl<Grid>("Root") is { } root)
        {
            _overviewRuler = new DiagnosticOverviewRuler(editor);
            Grid.SetColumn(_overviewRuler, 1);
            root.Children.Add(_overviewRuler);
        }
    }

    private static IAppSettingsService? TryGetSettings()
    {
        try { return AppServices.Provider.GetService<IAppSettingsService>(); }
        catch { return null; } // design-time / no container
    }

    private void OnAppSettingsChanged(object? sender, EventArgs e)
    {
        if (_editor is not null) ApplyAppSettings(_editor);
    }

    // Applies the user's editor preferences (font, line numbers, indentation, …).
    private void ApplyAppSettings(TextEditor editor)
    {
        var s = (_settings ?? TryGetSettings())?.Current ?? AppSettings.Default;
        editor.FontSize = s.EditorFontSize;
        editor.ShowLineNumbers = s.ShowLineNumbers;
        editor.Options.HighlightCurrentLine = s.HighlightCurrentLine;
        editor.Options.ConvertTabsToSpaces = s.ConvertTabsToSpaces;
        editor.Options.IndentationSize = Math.Max(1, s.IndentationSize);
        editor.WordWrap = s.EditorWordWrap; // #7

    }

    private bool IsDark() => ActualThemeVariant == ThemeVariant.Dark;

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        _colorizer?.SetVariant(IsDark() ? TherionColorizer.Variant.Dark : TherionColorizer.Variant.Light);
        _editor?.TextArea.TextView.Redraw();
    }

    // Push user edits back to the bindable Text property so a TwoWay binding
    // flushes them to the document model (which re-parses).
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        SetCurrentValue(TextProperty, _editor.Text);
        UpdateFoldings();
        UpdateThconfigDecorations();
        RecomputeFileWarnings();
        _overviewRuler?.InvalidateVisual();
    }

    private void UpdateFoldings()
    {
        if (_editor?.Document is null || _foldingManager is null) return;
        var foldings = TherionFoldingStrategy.CreateFoldings(_editor.Document);
        _foldingManager.UpdateFoldings(foldings, -1);
    }

    private bool IsThconfig()
    {
        var p = CurrentFilePath;
        if (string.IsNullOrEmpty(p)) return false;
        var ext = Path.GetExtension(p).ToLowerInvariant();
        return ext is ".thconfig" or ".thc";
    }

    // For .thconfig, leave the metapost/tex body inside layout…endlayout unhighlighted.
    private void UpdateThconfigDecorations()
    {
        if (_editor?.Document is null || _colorizer is null) return;
        _colorizer.SetSkipLines(IsThconfig()
            ? TherionFoldingStrategy.LayoutBodyLines(_editor.Document)
            : null);
        _editor.TextArea.TextView.Redraw();
    }

    /// <summary>Autocomplete vocabulary (Therion keywords + station/survey names).</summary>
    public IEnumerable<string>? CompletionTerms
    {
        get => GetValue(CompletionTermsProperty);
        set => SetValue(CompletionTermsProperty, value);
    }

    /// <summary>Optional diagnostic list — drives squiggle adornments + the overview ruler.</summary>
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

    /// <summary>
    /// Scrolls to a <see cref="SourceSpan"/> and briefly flashes its range (capped to
    /// the first line). Highlighting uses the transient flash adornment rather than a
    /// text selection, so navigating never leaves a multi-line selection behind (#6).
    /// </summary>
    public void ScrollTo(SourceSpan span)
    {
        if (_editor is null || span.IsEmpty) return;
        var doc = _editor.Document;
        int line = Math.Max(1, span.Start.Line);
        if (line > doc.LineCount) return;

        int startOffset = doc.GetOffset(line, Math.Max(1, span.Start.Column));
        var docLine = doc.GetLineByNumber(line);
        int spanEnd = span.End.Line >= 1 && span.End.Line <= doc.LineCount
            ? doc.GetOffset(span.End.Line, Math.Max(1, span.End.Column))
            : startOffset;
        // Keep the highlight to a single line for readability.
        int endOffset = Math.Min(Math.Max(spanEnd, startOffset), docLine.EndOffset);
        int length = endOffset - startOffset;
        if (length <= 0) length = Math.Max(0, docLine.EndOffset - startOffset);

        _editor.SelectionLength = 0;       // never leave a text selection from navigation (#6)
        _editor.CaretOffset = startOffset;
        _editor.ScrollToLine(line);
        _flash?.Flash(startOffset, length);
        _editor.Focus();
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
            if (_editor.Text != v)
            {
                _editor.Text = v;
                UpdateFoldings();
                UpdateThconfigDecorations();
                RecomputeFileWarnings();
                _overviewRuler?.InvalidateVisual();
            }
        }
        else if (change.Property == DiagnosticsProperty)
        {
            _boundDiagnostics = change.GetNewValue<IReadOnlyList<Diagnostic>?>() ?? Array.Empty<Diagnostic>();
            PushDiagnostics();
        }
        else if (change.Property == CurrentFilePathProperty)
        {
            var path = change.GetNewValue<string?>();
            _squiggles?.SetFilePath(path);
            _overviewRuler?.SetFilePath(path);
            UpdateThconfigDecorations();
            RecomputeFileWarnings();
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (e.Key == Key.F12 && shift)
        {
            FindReferencesAt(_editor.CaretOffset);
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            TryNavigateAt(_editor.CaretOffset);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && ctrl)
        {
            ShowCompletion(explicitTrigger: true);
            e.Handled = true;
        }
        else if (e.Key == Key.G && ctrl)
        {
            ShowGoToLine();
            e.Handled = true;
        }
        else if (ctrl && (e.Key == Key.OemQuestion || e.Key == Key.Divide))
        {
            ToggleLineComment();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: > 0 } t) return;

        // Bracket / quote auto-pairing (the opening char was already inserted).
        if (TryAutoPair(t)) return;

        // Auto-suggest as soon as a word is being typed; the window filters live.
        if (char.IsLetter(t[0]) || t[0] == '-')
            ShowCompletion(explicitTrigger: false);
    }

    private bool TryAutoPair(string typed)
    {
        if (_editor is null) return false;
        var close = typed switch { "\"" => "\"", "[" => "]", "(" => ")", _ => (string?)null };
        if (close is null) return false;
        int caret = _editor.CaretOffset;
        _editor.Document.Insert(caret, close);
        _editor.CaretOffset = caret; // place caret between the pair
        return true;
    }

    // ----- autocomplete --------------------------------------------------

    private void ShowCompletion(bool explicitTrigger)
    {
        if (_editor is null || _completionWindow is not null) return;

        int caret = _editor.CaretOffset;
        var text = _editor.Text;
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        var prefix = text.Substring(start, caret - start);
        if (!explicitTrigger && prefix.Length == 0) return;

        var candidates = CandidateTerms(start);
        if (candidates.Count == 0) return;

        var matches = candidates
            .Where(term => prefix.Length == 0 || term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Picks the completion vocabulary based on what precedes the caret on the line.</summary>
    private IReadOnlyList<string> CandidateTerms(int wordStart)
    {
        if (_editor is null) return Array.Empty<string>();
        var line = _editor.Document.GetLineByOffset(wordStart);
        var before = _editor.Document.GetText(line.Offset, wordStart - line.Offset);
        var tokens = before.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // First word on the line → command keywords.
        if (tokens.Length == 0)
            return TokenClassifier.Keywords.ToList();

        switch (tokens[0].ToLowerInvariant())
        {
            case "flags":
                return FlagValues;
            case "data":
                // 2nd token is the style (normal/diving/…); the rest are field names.
                return tokens.Length == 1 ? DataStyles : DataFields;
            case "input":
            case "load":
            case "encoding":
                return Array.Empty<string>(); // file paths / charsets — nothing useful to suggest
            default:
                // Inside a command/data row → station & survey names (+ keywords).
                return (CompletionTerms ?? Array.Empty<string>()).ToList();
        }
    }

    // ----- find references / go to line / toggle comment -----------------

    private string? _refWord;
    private int _refIndex;

    private void FindReferencesAt(int offset)
    {
        if (_editor is null || Navigation is null) return;
        var word = WordAt(_editor.Text, offset);
        if (string.IsNullOrEmpty(word)) return;

        var all = Navigation.FindReferences(word);
        if (all.IsDefaultOrEmpty) return;

        // Prefer references in the current file so navigation stays in this editor.
        var refs = string.IsNullOrEmpty(CurrentFilePath)
            ? all.ToList()
            : all.Where(s => string.Equals(s.FilePath, CurrentFilePath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (refs.Count == 0) refs = all.ToList();

        if (!string.Equals(word, _refWord, StringComparison.Ordinal)) { _refWord = word; _refIndex = 0; }
        else _refIndex = (_refIndex + 1) % refs.Count;

        var span = refs[_refIndex];
        var ln = Math.Max(1, span.Start.Line);
        if (ln > _editor.Document.LineCount) return;
        _editor.CaretOffset = _editor.Document.GetOffset(ln, Math.Max(1, span.Start.Column));
        _editor.ScrollToLine(ln);
        _editor.Focus();
    }

    private async void ShowGoToLine()
    {
        if (_editor is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var box = new TextBox { PlaceholderText = "Line number", Width = 200 };
        var dialog = new Window
        {
            Title = "Go to Line",
            Width = 240,
            Height = 96,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(10), Spacing = 8, Children = { box } },
        };
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) dialog.Close(box.Text);
            else if (ke.Key == Key.Escape) dialog.Close(null);
        };
        box.Loaded += (_, _) => box.Focus();

        var result = await dialog.ShowDialog<string?>(owner);
        if (int.TryParse(result?.Trim(), out var lineNumber))
            ScrollTo(lineNumber, 1);
    }

    private void ToggleLineComment()
    {
        if (_editor is null) return;
        var doc = _editor.Document;

        int startLine, endLine;
        if (_editor.SelectionLength > 0)
        {
            startLine = doc.GetLineByOffset(_editor.SelectionStart).LineNumber;
            endLine = doc.GetLineByOffset(_editor.SelectionStart + _editor.SelectionLength).LineNumber;
        }
        else
        {
            startLine = endLine = doc.GetLineByOffset(_editor.CaretOffset).LineNumber;
        }

        // Comment unless every non-blank line in range is already commented.
        bool allCommented = true;
        for (int i = startLine; i <= endLine; i++)
        {
            var t = doc.GetText(doc.GetLineByNumber(i)).TrimStart();
            if (t.Length == 0) continue;
            if (!t.StartsWith("#", StringComparison.Ordinal)) { allCommented = false; break; }
        }

        doc.BeginUpdate();
        try
        {
            for (int i = startLine; i <= endLine; i++)
            {
                var docLine = doc.GetLineByNumber(i);
                var text = doc.GetText(docLine);
                if (text.Trim().Length == 0) continue;
                int ws = 0;
                while (ws < text.Length && (text[ws] == ' ' || text[ws] == '\t')) ws++;

                if (allCommented)
                {
                    if (ws < text.Length && text[ws] == '#')
                    {
                        int removeLen = (ws + 1 < text.Length && text[ws + 1] == ' ') ? 2 : 1;
                        doc.Remove(docLine.Offset + ws, removeLen);
                    }
                }
                else
                {
                    doc.Insert(docLine.Offset + ws, "# ");
                }
            }
        }
        finally
        {
            doc.EndUpdate();
        }
    }

    // ----- diagnostic hover tooltip --------------------------------------

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_editor is null || _squiggles is null) return;
        var view = _editor.TextArea.TextView;

        var visualPos = e.GetPosition(view) + view.ScrollOffset;

        // Hyperlink affordance: underline + hand cursor over a navigable token (only
        // when the pointer is genuinely over the glyphs, not anywhere on the line).
        UpdateHoverLink(view, visualPos);

        var pos = view.GetPositionFloor(visualPos);
        if (pos is null) { ClearHover(); return; }

        var offset = _editor.Document.GetOffset(pos.Value.Location);
        var diag = _squiggles.GetDiagnosticAt(offset);
        if (diag is { } d)
        {
            if (!ReferenceEquals(_hoverDiagnostic, d))
            {
                _hoverDiagnostic = d;
                var tip = string.IsNullOrEmpty(d.Hint) ? d.Message : $"{d.Message}\n{d.Hint}";
                ToolTip.SetTip(_editor, $"{d.Severity}: {tip}");
                ToolTip.SetIsOpen(_editor, true);
            }
        }
        else
        {
            ClearHover();
        }
    }

    private void ClearHover()
    {
        if (_editor is null || _hoverDiagnostic is null) return;
        _hoverDiagnostic = null;
        ToolTip.SetIsOpen(_editor, false);
    }

    // ----- occurrence highlight + caret reporting ------------------------

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;

        // Dim-highlight every occurrence of the identifier under the caret/selection (#10).
        if (_occurrences?.SetWord(SelectedOrCaretWord()) == true)
            _editor.TextArea.TextView.InvalidateVisual();

        // Report the caret location for the navigation history (#1).
        if (!string.IsNullOrEmpty(CurrentFilePath))
        {
            var loc = _editor.Document.GetLocation(_editor.CaretOffset);
            CaretMoved?.Invoke(this, new SourceSpan(
                CurrentFilePath!, new SourceLocation(loc.Line, loc.Column),
                new SourceLocation(loc.Line, loc.Column), _editor.CaretOffset, 0));
        }
    }

    /// <summary>The whole-word identifier to highlight: the selection if it is one, else the word at the caret.</summary>
    private string? SelectedOrCaretWord()
    {
        if (_editor is null) return null;
        if (_editor.SelectionLength > 0)
        {
            var sel = _editor.SelectedText;
            return IsIdentifier(sel) ? sel : null;
        }
        var w = WordAt(_editor.Text, _editor.CaretOffset);
        return w.Length >= 2 ? w : null;
    }

    private static bool IsIdentifier(string s)
    {
        if (s.Length < 2) return false;
        foreach (var c in s) if (!IsWordChar(c)) return false;
        return true;
    }

    // ----- view-state persistence (keep caret/scroll across tab switches, #11) -----

    /// <summary>Restores a previously-saved caret offset and scrolls it into view.</summary>
    public void RestoreCaret(int caret)
    {
        if (_editor is null || caret < 0 || caret > _editor.Document.TextLength) return;
        _editor.CaretOffset = caret;
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(caret).LineNumber);
    }

    private void OnEditorPointerExited(object? sender, PointerEventArgs e)
    {
        if (_editor is null) return;
        if (_hyperlinkColorizer?.Clear() == true)
        {
            _editor.TextArea.TextView.Cursor = null;
            _editor.TextArea.TextView.Redraw();
        }
    }

    // ----- go to definition / linked files -------------------------------

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null) return;
        var view = _editor.TextArea.TextView;
        var visualPos = e.GetPosition(view) + view.ScrollOffset;
        if (FloorOffset(visualPos) is not { } off) return;

        bool leftButton = e.GetCurrentPoint(_editor).Properties.IsLeftButtonPressed;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (!leftButton && !ctrl) return;

        // Path hyperlink (input/load/source open in-editor; export output via the OS).
        if (ResolvePathLinkAt(off) is { } link &&
            PointerWithinRange(view, link.Start, link.Start + link.Length, visualPos.X))
        {
            OpenLink(link);
            e.Handled = true;
            return;
        }

        // Reference link: navigate when the pointer is over a resolvable token
        // (plain click, like a hyperlink — covers @-refs, stations, map scraps).
        if (RefTokenUnderPointer(visualPos) is { } hit)
        {
            var kind = ChooseReferenceKind((hit.Start, hit.Length, hit.Raw), hit.Offset);
            if ((ctrl || Navigation?.CanNavigate(hit.Raw, kind) == true) &&
                Navigation?.GoToDefinition(hit.Raw, kind) is { } span && !span.IsEmpty)
            {
                NavigateToSpan(span);
                e.Handled = true;
            }
        }
    }

    /// <summary>Document offset of the glyph under <paramref name="visualPos"/>, or null if past the text.</summary>
    private int? FloorOffset(Point visualPos)
    {
        if (_editor is null) return null;
        var loc = _editor.TextArea.TextView.GetPositionFloor(visualPos);
        return loc is null ? null : _editor.Document.GetOffset(loc.Value.Location);
    }

    private void UpdateHoverLink(TextView view, Point visualPos)
    {
        if (_editor is null || _hyperlinkColorizer is null) return;
        bool changed;
        if (HoverLinkAt(visualPos) is { } link)
        {
            changed = _hyperlinkColorizer.SetLink(link.Start, link.Length);
            view.Cursor = _handCursor;
        }
        else
        {
            changed = _hyperlinkColorizer.Clear();
            view.Cursor = null;
        }
        if (changed) view.Redraw();
    }

    /// <summary>
    /// The navigable sub-range under the pointer: a file path, or — for an
    /// <c>a@b</c> reference — only the half the pointer is over (so it's clear which
    /// part will be navigated, item #14). Returns null unless the token resolves.
    /// Also reports the resolved target via <see cref="HoverTargetChanged"/> (#8).
    /// </summary>
    private (int Start, int Length)? HoverLinkAt(Point visualPos)
    {
        var view = _editor!.TextArea.TextView;
        if (FloorOffset(visualPos) is { } off)
        {
            // Path links (input/load/source/export output) — keyword itself excluded.
            if (ResolvePathLinkAt(off) is { } link &&
                PointerWithinRange(view, link.Start, link.Start + link.Length, visualPos.X))
            {
                RaiseHoverTarget(link.ResolvedExisting is { } p
                    ? new SourceSpan(p, SourceLocation.Start, SourceLocation.Start, 0, 0) : null);
                return (link.Start, link.Length);
            }
        }

        if (Navigation is not null && RefTokenUnderPointer(visualPos) is { } hit)
        {
            var kind = ChooseReferenceKind((hit.Start, hit.Length, hit.Raw), hit.Offset);
            if (Navigation.CanNavigate(hit.Raw, kind))
            {
                RaiseHoverTarget(Navigation.GoToDefinition(hit.Raw, kind));
                return RefPartRange((hit.Start, hit.Length, hit.Raw), hit.Offset);
            }
        }

        RaiseHoverTarget(null);
        return null;
    }

    /// <summary>Raised (debounced via change-detection) with the workspace target under the pointer (#8).</summary>
    public event EventHandler<SourceSpan?>? HoverTargetChanged;
    private string? _lastHoverKey;

    private void RaiseHoverTarget(SourceSpan? target)
    {
        var key = target is { } s ? $"{s.FilePath}|{s.StartOffset}" : null;
        if (key == _lastHoverKey) return;
        _lastHoverKey = key;
        HoverTargetChanged?.Invoke(this, target);
    }

    private void TryNavigateAt(int offset)
    {
        if (_editor is null) return;
        // Linked-file navigation takes priority over symbol go-to-definition.
        if (ResolvePathLinkAt(offset) is { } link) { OpenLink(link); return; }
        if (Navigation is null) return;

        if (ExtractRefToken(_editor.Text, offset) is not { } tok) return;
        var kind = ChooseReferenceKind(tok, offset);
        var span = Navigation.GoToDefinition(tok.Raw, kind);
        if (span is null || span.Value.IsEmpty) return;

        NavigateToSpan(span.Value);
    }

    /// <summary>
    /// The reference token under the pointer, but only when the pointer is actually over
    /// the glyphs (not whitespace or virtual space past the line end) — items #12/#13.
    /// </summary>
    private (int Start, int Length, string Raw, int Offset)? RefTokenUnderPointer(Point visualPos)
    {
        if (_editor is null) return null;
        var view = _editor.TextArea.TextView;
        var loc = view.GetPositionFloor(visualPos);
        if (loc is null) return null;
        int off = _editor.Document.GetOffset(loc.Value.Location);
        if (ExtractRefToken(_editor.Text, off) is not { } tok) return null;
        if (!PointerWithinRange(view, tok.Start, tok.Start + tok.Length, visualPos.X)) return null;
        return (tok.Start, tok.Length, tok.Raw, off);
    }

    /// <summary>True when the pointer X lies within the glyph range of [start, end) on its line.</summary>
    private bool PointerWithinRange(TextView view, int startOffset, int endOffset, double x)
    {
        if (_editor is null) return false;
        var doc = _editor.Document;
        endOffset = Math.Min(endOffset, doc.TextLength);
        var startX = view.GetVisualPosition(new TextViewPosition(doc.GetLocation(startOffset)), VisualYPosition.TextTop).X;
        var endX = view.GetVisualPosition(new TextViewPosition(doc.GetLocation(endOffset)), VisualYPosition.TextTop).X;
        return x >= startX - 1 && x <= endX + 1;
    }

    /// <summary>For an <c>a@b</c> token returns the half under the caret; otherwise the whole token.</summary>
    private static (int Start, int Length) RefPartRange((int Start, int Length, string Raw) tok, int offset)
    {
        int at = tok.Raw.IndexOf('@');
        if (at < 0) return (tok.Start, tok.Length);
        return offset - tok.Start <= at
            ? (tok.Start, at)                              // point half
            : (tok.Start + at + 1, tok.Length - at - 1);  // survey half
    }

    /// <summary>
    /// Routes every navigation through the shell (document service) so the back/forward
    /// history records each jump (#3), even within the same file. The service scrolls
    /// this editor for same-file targets and opens+scrolls for cross-file ones.
    /// </summary>
    private void NavigateToSpan(SourceSpan span)
    {
        if (!span.IsEmpty) NavigateToSpanRequested?.Invoke(this, span);
    }

    /// <summary>
    /// Decides what a clicked reference token resolves to: the survey half of
    /// <c>point@survey</c> (caret right of the <c>@</c>), else the kind implied by the
    /// command keyword on the line (<c>select</c>→map, <c>join</c>→scrap object, else station).
    /// </summary>
    private ReferenceKind ChooseReferenceKind((int Start, int Length, string Raw) tok, int caretOffset)
    {
        int at = tok.Raw.IndexOf('@');
        if (at >= 0 && caretOffset - tok.Start > at) return ReferenceKind.Survey;

        // Point/bare half: the keyword narrows it; otherwise let the resolver try every
        // kind (station first, then survey/map/scrap) so map scraps and .th2 stations work.
        return LineKeyword(tok.Start) switch
        {
            "select" => ReferenceKind.Map,
            "join"   => ReferenceKind.ScrapObject,
            _        => ReferenceKind.Any,
        };
    }

    /// <summary>The lower-cased first word on the physical line containing <paramref name="offset"/>.</summary>
    private string LineKeyword(int offset)
    {
        if (_editor is null) return string.Empty;
        var line = _editor.Document.GetLineByOffset(offset);
        return FirstWordRaw(_editor.Document.GetText(line)).ToLowerInvariant();
    }

    /// <summary>Extracts the reference token (id, <c>@</c>, survey path) surrounding <paramref name="offset"/>.</summary>
    private static (int Start, int Length, string Raw)? ExtractRefToken(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return null;
        int start = offset;
        while (start > 0 && IsRefChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsRefChar(text[end])) end++;
        return end > start ? (start, end - start, text.Substring(start, end - start)) : null;
    }

    // A Therion reference: station chars plus '@' (survey qualifier) and ':' (join mark).
    private static bool IsRefChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '@' or ':';

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

    // ----- file-path hyperlinks (input / load / source / export / system) -----

    /// <summary>
    /// If <paramref name="offset"/> is over a navigable file path on a path-bearing
    /// command line, returns its document range and resolution. The path is a link
    /// even when the target doesn't exist (so relative paths stay navigable).
    /// </summary>
    private PathLink? ResolvePathLinkAt(int offset)
    {
        if (_editor is null || string.IsNullOrEmpty(CurrentFilePath)) return null;
        var text = _editor.Text;
        if (ExtractPathToken(text, offset) is not { } tok) return null;

        var raw = tok.Raw.Trim();
        if (raw.Length == 0) return null;

        var line = _editor.Document.GetLineByOffset(offset);
        var lineText = _editor.Document.GetText(line);
        var keyword = FirstWordRaw(lineText).ToLowerInvariant();
        if (!PathCommands.Contains(keyword)) return null;

        // The keyword itself (e.g. "input") is never a link — only its path argument.
        int kwStart = line.Offset + (lineText.Length - lineText.TrimStart().Length);
        if (offset < kwStart + keyword.Length) return null;

        // On export/system lines only the OUTPUT file is a link — not map/-projection/
        // plan/-output etc. (#16) — and it opens in the OS default app (#15).
        bool shellOpen = false;
        if (keyword is "export" or "system")
        {
            if (!IsOutputValueToken(line, lineText, tok.Start)) return null;
            shellOpen = true;
        }

        var full = ComputeFullPath(raw);
        if (full is null) return null;
        return new PathLink(tok.Start, tok.Length, raw, FindExisting(full), full,
            Expected: ExpectedFileCommands.Contains(keyword), ShellOpen: shellOpen);
    }

    /// <summary>True when the token at <paramref name="tokenStart"/> is the value right after <c>-output</c>/<c>-o</c>.</summary>
    private static bool IsOutputValueToken(AvaloniaEdit.Document.DocumentLine line, string lineText, int tokenStart)
    {
        int tokCol = tokenStart - line.Offset;
        var words = System.Text.RegularExpressions.Regex.Matches(lineText, @"\S+");
        for (int i = 0; i < words.Count - 1; i++)
        {
            if (words[i].Value.Equals("-output", StringComparison.OrdinalIgnoreCase) ||
                words[i].Value.Equals("-o", StringComparison.OrdinalIgnoreCase))
            {
                var next = words[i + 1];
                if (tokCol >= next.Index && tokCol < next.Index + next.Length) return true;
            }
        }
        return false;
    }

    private void OpenLink(PathLink link)
    {
        if (link.ResolvedExisting is { } existing)
        {
            if (link.ShellOpen) OpenExternalRequested?.Invoke(this, existing);
            else OpenFileRequested?.Invoke(this, existing);
        }
        else
        {
            _ = ShowMessageAsync(link.ShellOpen ? "Output not found" : "File not found",
                $"The referenced file could not be found:\n\n{link.AttemptedFull}");
        }
    }

    private async System.Threading.Tasks.Task ShowMessageAsync(string title, string message)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var ok = new Button { Content = "OK", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }

    /// <summary>Extracts the (possibly quoted) path-like token surrounding <paramref name="offset"/>.</summary>
    private static (int Start, int Length, string Raw)? ExtractPathToken(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return null;

        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n') lineStart--;
        int lineEnd = offset;
        while (lineEnd < text.Length && text[lineEnd] != '\n') lineEnd++;

        // Quoted path: "…"
        for (int p = lineStart; p < lineEnd; p++)
        {
            if (text[p] != '"') continue;
            int q = text.IndexOf('"', p + 1);
            if (q < 0 || q > lineEnd) break;
            if (offset >= p && offset <= q)
                return (p + 1, q - (p + 1), text.Substring(p + 1, q - (p + 1)));
            p = q;
        }

        // Unquoted: expand over path characters around the offset.
        bool here = offset < lineEnd && IsPathChar(text[offset]);
        bool before = offset > lineStart && IsPathChar(text[offset - 1]);
        if (!here && !before) return null;

        int s = offset;
        while (s > lineStart && IsPathChar(text[s - 1])) s--;
        int e = offset;
        while (e < lineEnd && IsPathChar(text[e])) e++;
        return e > s ? (s, e - s, text.Substring(s, e - s)) : null;
    }

    private static bool IsPathChar(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '/' or '\\' or ':' or '~';

    /// <summary>Resolves a raw path to an absolute path (relative to the current file) without checking existence.</summary>
    private string? ComputeFullPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath)) return null;
        var baseDir = Path.GetDirectoryName(CurrentFilePath);
        if (string.IsNullOrEmpty(baseDir)) return null;

        var rel = raw.Replace('/', Path.DirectorySeparatorChar);
        try { return Path.IsPathRooted(rel) ? Path.GetFullPath(rel) : Path.GetFullPath(Path.Combine(baseDir, rel)); }
        catch { return null; }
    }

    /// <summary>Returns the existing file for an absolute path, trying the implicit .th / .th2 extensions.</summary>
    private static string? FindExisting(string full)
    {
        if (File.Exists(full)) return full;
        // Therion source paths often omit the .th / .th2 extension.
        if (!Path.HasExtension(full))
        {
            if (File.Exists(full + ".th")) return full + ".th";
            if (File.Exists(full + ".th2")) return full + ".th2";
        }
        return null;
    }

    // ----- diagnostics: merge parser diagnostics with missing-file warnings -----

    private void PushDiagnostics()
    {
        List<Diagnostic> merged = _fileWarnings.Count == 0
            ? new List<Diagnostic>(_boundDiagnostics)
            : new List<Diagnostic>(_boundDiagnostics.Count + _fileWarnings.Count);
        if (_fileWarnings.Count != 0) { merged.AddRange(_boundDiagnostics); merged.AddRange(_fileWarnings); }
        _squiggles?.SetDiagnostics(merged);
        _overviewRuler?.SetDiagnostics(merged);
    }

    // Flags input/load/source lines whose referenced file is missing (a file we
    // expect to exist). Export/system output paths are intentionally not checked.
    private void RecomputeFileWarnings()
    {
        var warnings = new List<Diagnostic>();
        if (_editor?.Document is { } doc && !string.IsNullOrEmpty(CurrentFilePath))
        {
            var text = _editor.Text;
            foreach (var line in doc.Lines)
            {
                var lineText = doc.GetText(line);
                var keyword = FirstWordRaw(lineText);
                if (!ExpectedFileCommands.Contains(keyword)) continue;

                // Position of the first argument: leading whitespace + keyword, then
                // skip the whitespace separating the keyword from its path argument.
                int lead = 0;
                while (lead < lineText.Length && char.IsWhiteSpace(lineText[lead])) lead++;
                int argCol = lead + keyword.Length;
                while (argCol < lineText.Length && char.IsWhiteSpace(lineText[argCol])) argCol++;
                if (argCol >= lineText.Length) continue;

                if (ExtractPathToken(text, line.Offset + argCol) is not { } tok) continue;
                var full = ComputeFullPath(tok.Raw.Trim());
                if (full is null || FindExisting(full) is not null) continue;

                warnings.Add(MissingFileWarning(doc, tok, keyword));
            }
        }
        _fileWarnings = warnings;
        PushDiagnostics();
    }

    private Diagnostic MissingFileWarning(TextDocument doc, (int Start, int Length, string Raw) tok, string keyword)
    {
        var startLoc = doc.GetLocation(tok.Start);
        var endLoc = doc.GetLocation(Math.Min(tok.Start + tok.Length, doc.TextLength));
        var span = new SourceSpan(
            CurrentFilePath!,
            new SourceLocation(startLoc.Line, startLoc.Column),
            new SourceLocation(endLoc.Line, endLoc.Column),
            tok.Start, tok.Length);
        return Diagnostic.Create(
            "TH_LINK_404",
            DiagnosticSeverity.Warning,
            $"Referenced file not found: '{tok.Raw.Trim()}'.",
            span,
            hint: $"'{keyword}' expects an existing file.");
    }

    private static string FirstWordRaw(string text)
    {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        return text.Substring(start, i - start);
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

    /// <summary>
    /// Paints a transient amber highlight over a navigation target, holding briefly then
    /// fading out over ~1.5s total. Self-disposing: stops its own timer once invisible.
    /// </summary>
    private sealed class FlashHighlightRenderer : IBackgroundRenderer
    {
        private static readonly Color FlashColor = Color.FromRgb(0xFF, 0xC1, 0x07); // amber

        private readonly TextView _view;
        private TextSegment? _segment;
        private double _opacity;
        private DispatcherTimer? _timer;

        public FlashHighlightRenderer(TextView view) => _view = view;

        public KnownLayer Layer => KnownLayer.Selection;

        public void Flash(int offset, int length)
        {
            if (length <= 0) length = 1;
            _segment = new TextSegment { StartOffset = offset, Length = length };
            _opacity = 0.85;

            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            int ticks = 0;
            _timer.Tick += (_, _) =>
            {
                // Hold the full highlight for ~0.6s (12 ticks), then fade over ~0.9s.
                if (++ticks > 12) _opacity -= 0.05;
                if (_opacity <= 0)
                {
                    _opacity = 0;
                    _segment = null;
                    _timer?.Stop();
                    _timer = null;
                }
                _view.InvalidateVisual();
            };
            _timer.Start();
            _view.InvalidateVisual();
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_segment is null || _opacity <= 0) return;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            builder.AddSegment(textView, _segment);
            if (builder.CreateGeometry() is { } geometry)
                drawingContext.DrawGeometry(new SolidColorBrush(FlashColor, _opacity), null, geometry);
        }
    }

    /// <summary>
    /// Dim background highlight of every whole-word occurrence of the identifier under
    /// the caret, drawn across the visible region only (item #10).
    /// </summary>
    private sealed class OccurrenceHighlightRenderer : IBackgroundRenderer
    {
        private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x38, 0x80, 0x80, 0x80));

        private readonly TextView _view;
        private string? _word;

        public OccurrenceHighlightRenderer(TextView view) => _view = view;

        public KnownLayer Layer => KnownLayer.Selection;

        /// <summary>Sets the word to highlight; returns true when it changed (caller should redraw).</summary>
        public bool SetWord(string? word)
        {
            word = string.IsNullOrEmpty(word) ? null : word;
            if (string.Equals(word, _word, StringComparison.Ordinal)) return false;
            _word = word;
            return true;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_word is null || textView.Document is not { } doc || textView.VisualLines.Count == 0) return;

            int viewStart = textView.VisualLines[0].FirstDocumentLine.Offset;
            int viewEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            if (viewEnd <= viewStart) return;

            var text = doc.GetText(viewStart, viewEnd - viewStart);
            int idx = 0;
            while ((idx = text.IndexOf(_word, idx, StringComparison.Ordinal)) >= 0)
            {
                int start = viewStart + idx;
                int end = start + _word.Length;
                if (IsWholeWord(doc, start, end))
                {
                    var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
                    builder.AddSegment(textView, new TextSegment { StartOffset = start, Length = _word.Length });
                    if (builder.CreateGeometry() is { } geometry)
                        drawingContext.DrawGeometry(Fill, null, geometry);
                }
                idx += _word.Length;
            }
        }

        private static bool IsWholeWord(TextDocument doc, int start, int end) =>
            (start == 0 || !IsWordChar(doc.GetCharAt(start - 1))) &&
            (end >= doc.TextLength || !IsWordChar(doc.GetCharAt(end)));
    }
}
