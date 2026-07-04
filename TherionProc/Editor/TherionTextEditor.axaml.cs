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
using Avalonia.Input.Platform;
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

    /// <summary>When false, syntax highlighting + hover/link features are disabled (large files, #10).</summary>
    public static readonly StyledProperty<bool> HighlightingEnabledProperty =
        AvaloniaProperty.Register<TherionTextEditor, bool>(nameof(HighlightingEnabled), defaultValue: true);

    /// <summary>Raised when the user activates a file-path hyperlink (input/load/source); carries the resolved file path.</summary>
    public event EventHandler<string>? OpenFileRequested;

    /// <summary>Raised when an export/output file link is activated; it should open in the OS default app (#15).</summary>
    public event EventHandler<string>? OpenExternalRequested;

    /// <summary>Raised when a cross-file reference resolves to a span in a different file.</summary>
    public event EventHandler<SourceSpan>? NavigateToSpanRequested;

    /// <summary>Raised when the caret moves, carrying its document span (for navigation history).</summary>
    public event EventHandler<SourceSpan>? CaretMoved;

    /// <summary>
    /// True while the caret is being moved by highlighted-term navigation (Shift+F12 cycling
    /// through references). Such moves are excluded from the back/forward history (#1).
    /// </summary>
    public bool IsTermNavigating { get; private set; }

    /// <summary>
    /// True when the most recent caret move was caused by a left-button pointer click — a
    /// deliberate "place the cursor here" navigation point, recorded in history even for short
    /// moves so back/forward behaves like a normal editor (#8).
    /// </summary>
    public bool LastCaretMoveFromPointer { get; private set; }
    private bool _caretFromPointer;

    /// <summary>true when the latest caret move was driven by the in-editor search panel
    /// (find next/previous), which must not pollute the back/forward navigation history.</summary>
    public bool LastCaretMoveFromSearch { get; private set; }

    /// <summary>in-process drag format carrying a file path from the Workspace Explorer;
    /// dropping it on the editor inserts an <c>input</c>/<c>source</c> line for that file.</summary>
    public static readonly Avalonia.Input.DataFormat<string> InsertPathFormat =
        Avalonia.Input.DataFormat.CreateInProcessFormat<string>("therionproc-insert-path");

    /// <summary>Raised when the user requests "find all references" for an identifier (#4).</summary>
    public event EventHandler<string>? FindReferencesRequested;

    /// <summary>Raised when the user requests a symbol rename (F2 / context menu / hover overlay).</summary>
    public event EventHandler<(string Raw, ReferenceKind Kind)>? RenameSymbolRequested;

    private const string ThbookUrl = "https://therion.speleo.sk/wiki/start";

    // Short descriptions for the hover overlay on command keywords (#4).
    private static readonly Dictionary<string, string> CommandDocs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["survey"]      = "survey <id> [-title …] … endsurvey — a named group (scope) of centrelines, maps and scraps.",
        ["centreline"]  = "centreline … endcentreline — survey shots (data rows) between stations.",
        ["centerline"]  = "centerline … endcentreline — survey shots (data rows) between stations.",
        ["data"]        = "data <style> <fields…> — declares the column layout for the following shot rows.",
        ["fix"]         = "fix <station> <x> <y> <z> [stdev…] — fixes a station at the given coordinates.",
        ["equate"]      = "equate <station> <station> [...] — declares that the listed stations are the same point.",
        ["station"]     = "station <name> \"comment\" [flags] — annotates a station (entrance, fixed, …).",
        ["flags"]       = "flags [not] surface|duplicate|splay|approximate — toggles flags for following shots.",
        ["join"]        = "join <obj1> <obj2> [-options] — connects scrap point/line objects (or scraps) by id.",
        ["input"]       = "input <path> — includes another source file.",
        ["load"]        = "load <path> — includes another source file.",
        ["map"]         = "map <id> [-projection …] … endmap — a collection of scraps or other maps of one projection.",
        ["scrap"]       = "scrap <id> [-projection …] … endscrap — a 2-D drawing fragment.",
        ["point"]       = "point <x> <y> <type> [-options] — a symbol in a scrap (station, label, …).",
        ["line"]        = "line <type> [-options] … endline — a poly-line in a scrap (wall, …).",
        ["area"]        = "area <type> … endarea — a filled area bounded by lines.",
        ["surface"]     = "surface … endsurface — terrain/DEM grid of elevation values.",
        ["group"]       = "group … endgroup — scopes settings/commands without forming a survey.",
        ["import"]      = "import <path> — imports surface/scrap data.",
        ["source"]      = "source <path> — (.thconfig) the survey data to process.",
        ["select"]      = "select <map@survey> — chooses what to export.",
        ["export"]      = "export <type> [-options] -output <file> — produces an output (map/model/…).",
        ["layout"]      = "layout <id> … endlayout — output styling (metapost/tex) block.",
        ["lookup"]      = "lookup … endlookup — opaque lookup block (body is not parsed).",
        ["encoding"]    = "encoding <charset> — declares the file's character set.",
    };

    private TextEditor? _editor;
    private TherionColorizer? _colorizer;
    private HyperlinkColorizer? _hyperlinkColorizer;
    private DiagnosticSquiggleRenderer? _squiggles;
    private DiagnosticOverviewRuler? _overviewRuler;
    private FlashHighlightRenderer? _flash;
    private OccurrenceHighlightRenderer? _occurrences;
    private Avalonia.Controls.Primitives.Popup? _infoPopup;
    private DispatcherTimer? _infoShowTimer;
    private DispatcherTimer? _infoHideTimer;
    private int _infoTokenStart = -1;
    private int _pendingNavOffset = -1;   // definition offset to collapse selection on PointerReleased (#2)
    private TopLevel? _windowForPopup;     // window-level pointer routing while popup is open (#3)
    private IBookmarksService? _bookmarks;
    private BookmarkHighlightRenderer? _bookmarkHighlighter;
    private BookmarkMargin? _bookmarkMargin;
    private Point _infoPointer;
    private bool _pointerOverPopup;   // pointer is currently inside the hover popup (#5)
    private FoldingManager? _foldingManager;
    private MatchingBlockRenderer? _matchRenderer;        // matching block opener/closer highlight
    private ColorAdornmentRenderer? _colorAdorn;          // inline color swatches
    private MinimapControl? _minimap;                     // code minimap
    private Dictionary<int, int> _blockPairs = new();     // opener↔closer line pairs
    private MenuItem? _matchMenuItem;                     // context-menu item (visibility gated)
    private MenuItem? _stepIntoMenuItem;                  // context-menu item (visibility gated)
    private MenuItem? _formatMenuItem;                    // context-menu item (visibility gated)
    private MenuItem? _peekMenuItem;                      // context-menu item (visibility gated)
    private MenuItem? _open3dMenuItem;                    // #5: "Open in 3D" (shown for station/survey refs)
    private string? _open3dName;                          // the station/survey under the right-click
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
            // Tunnel runs before AvaloniaEdit moves the caret, so we can flag the resulting caret
            // move as pointer-driven for the navigation history (#8).
            _editor.AddHandler(InputElement.PointerPressedEvent, OnEditorPointerPressedCapture,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            _editor.TextArea.TextView.PointerMoved += OnEditorPointerMoved;
            _editor.TextArea.TextView.PointerExited += OnEditorPointerExited;
            _editor.TextArea.TextView.AddHandler(InputElement.PointerReleasedEvent, OnEditorPointerReleased,
                RoutingStrategies.Bubble, handledEventsToo: true);
            // Move the caret to the right-clicked location before the context menu opens (tunnel
            // runs ahead of the ContextMenu's own bubbling open handler), like a typical editor.
            _editor.TextArea.AddHandler(InputElement.ContextRequestedEvent, OnEditorContextRequested,
                RoutingStrategies.Tunnel);
            // Tunnel KeyDown runs before AvaloniaEdit inserts a newline, so Smart Enter
            // can intercept Return and substitute a block-aware insertion.
            _editor.TextArea.AddHandler(InputElement.KeyDownEvent, OnTextAreaPreviewKeyDown,
                RoutingStrategies.Tunnel);
            _editor.TextArea.TextEntered += OnTextEntered;
            _editor.TextChanged += OnEditorTextChanged;
            ActualThemeVariantChanged += OnThemeVariantChanged;
            // Live-apply custom syntax color changes (#2).
            TherionColorizer.ColorsChanged += OnCustomColorsChanged;
            DetachedFromVisualTree += (_, _) => TherionColorizer.ColorsChanged -= OnCustomColorsChanged;
            // Track the most-recently focused editor so the shell Edit/Search menus can act on
            // the active document's editor (#11/#12).
            _editor.TextArea.GotFocus += (_, _) => LastFocused = this;
        }

        // Register this editor instance so the shell can target it by file even when unfocused (#9).
        AttachedToVisualTree += (_, _) => { if (!Instances.Contains(this)) Instances.Add(this); };
        DetachedFromVisualTree += (_, _) =>
        {
            Instances.Remove(this);
            if (ReferenceEquals(LastFocused, this)) LastFocused = null;
        };
    }

    /// <summary>The editor that last held focus — the target of the shell Edit/Search menus (#11/#12).</summary>
    public static TherionTextEditor? LastFocused { get; private set; }

    // Live editor instances, so the shell can target the ACTIVE document's editor even when it
    // was never focused (e.g. a file just opened via navigation) (#9).
    private static readonly System.Collections.Generic.List<TherionTextEditor> Instances = new();

    /// <summary>Returns the live editor bound to <paramref name="filePath"/>, if any (#9).</summary>
    public static TherionTextEditor? ForPath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        foreach (var ed in Instances)
            if (string.Equals(ed.CurrentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return ed;
        return null;
    }

    private SearchPanel? _searchPanel;

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

        // Rebuild the right-click menu when the UI language changes: its item headers are resolved
        // once via Tr.Get at build time, so without this the editor context menu would stay in the
        // previous language after a switch.
        TherionProc.Resources.LocProxy.Instance.PropertyChanged += OnUiLanguageChanged;
        DetachedFromVisualTree += (_, _) =>
            TherionProc.Resources.LocProxy.Instance.PropertyChanged -= OnUiLanguageChanged;

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
        editor.TextArea.SelectionChanged += OnSelectionChanged;   // status-bar selection stats

        // accept a file dragged from the Workspace Explorer → insert an input/source line.
        DragDrop.SetAllowDrop(editor, true);
        editor.AddHandler(DragDrop.DragOverEvent, OnEditorDragOver);
        editor.AddHandler(DragDrop.DropEvent, OnEditorDrop);

        // highlight the matching opener/closer of the block keyword under the caret.
        _matchRenderer = new MatchingBlockRenderer();
        editor.TextArea.TextView.BackgroundRenderers.Add(_matchRenderer);

        // draw a colour swatch next to colour values.
        _colorAdorn = new ColorAdornmentRenderer
        {
            Enabled = EditorFeatureFlags.IsEnabled(EditorFeature.ColorAdornments,
                (_settings ?? TryGetSettings())?.Current),
        };
        editor.TextArea.TextView.BackgroundRenderers.Add(_colorAdorn);

        // Transient highlight painted on a navigation arrival, then faded out.
        _flash = new FlashHighlightRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_flash);

        editor.TextArea.IndentationStrategy = new TherionIndentationStrategy();
        _searchPanel = SearchPanel.Install(editor);             // Ctrl+F / Ctrl+H
        _foldingManager = FoldingManager.Install(editor.TextArea);

        // Context menu (B2).
        editor.TextArea.ContextMenu = BuildContextMenu();

        // Bookmark margin renderer (B3).
        _bookmarkHighlighter = new BookmarkHighlightRenderer(editor.TextArea.TextView);
        editor.TextArea.TextView.BackgroundRenderers.Add(_bookmarkHighlighter);
        // A box marker in the left gutter beside the line number (B3 / #4).
        _bookmarkMargin = new BookmarkMargin();
        editor.TextArea.LeftMargins.Insert(0, _bookmarkMargin);
        try
        {
            _bookmarks = AppServices.Provider.GetService<IBookmarksService>();
            if (_bookmarks is not null)
                _bookmarks.BookmarksChanged += (_, _) => editor.TextArea.TextView.InvalidateVisual();
        }
        catch { /* design-time / no container */ }
        UpdateFoldings();
        UpdateThconfigDecorations();

        // Inject the overview ruler into column 1 of the layout grid.
        if (this.FindControl<Grid>("Root") is { } root)
        {
            _overviewRuler = new DiagnosticOverviewRuler(editor);
            Grid.SetColumn(_overviewRuler, 1);
            Grid.SetRow(_overviewRuler, 1); // added a breadcrumb row at the top
            root.Children.Add(_overviewRuler);

            // code minimap in the rightmost column (hidden until enabled).
            _minimap = new MinimapControl(editor) { IsVisible = false };
            Grid.SetColumn(_minimap, 2);
            Grid.SetRow(_minimap, 1);
            root.Children.Add(_minimap);
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
        UpdateBreadcrumb(); // may have been toggled
    }

    // The UI language changed: rebuild the context menu so its item headers pick up the new language
    // (they are baked in once at build time). Visibility of the feature-gated items is refreshed on
    // menu open, so pointing the fields at the freshly-built items is enough.
    private void OnUiLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_editor is not null)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _editor.TextArea.ContextMenu = BuildContextMenu());
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

        // whitespace / EOL / indent-guide rendering, gated by the feature flag.
        bool wsFeature = EditorFeatureFlags.IsEnabled(EditorFeature.WhitespaceGuides, s);
        editor.Options.ShowSpaces = wsFeature && s.EditorShowWhitespace;
        editor.Options.ShowTabs = wsFeature && s.EditorShowWhitespace;
        editor.Options.ShowEndOfLine = wsFeature && s.EditorShowEndOfLine;
        ApplyIndentGuides(editor, wsFeature && s.EditorShowIndentGuides, Math.Max(1, s.IndentationSize));

        // toggle the colour swatches with the feature flag.
        if (_colorAdorn is not null)
        {
            _colorAdorn.Enabled = EditorFeatureFlags.IsEnabled(EditorFeature.ColorAdornments, s);
            editor.TextArea.TextView.InvalidateVisual();
        }

        // show the minimap when enabled (feature flag + View-menu toggle).
        if (_minimap is not null)
            _minimap.IsVisible = EditorFeatureFlags.IsEnabled(EditorFeature.Minimap, s) && s.EditorShowMinimap;
    }

    private IndentGuideRenderer? _indentGuides;

    // Adds/removes the indent-guide background renderer to match the toggle.
    private void ApplyIndentGuides(TextEditor editor, bool enabled, int indentSize)
    {
        var renderers = editor.TextArea.TextView.BackgroundRenderers;
        if (enabled)
        {
            _indentGuides ??= new IndentGuideRenderer();
            _indentGuides.SetIndentSize(indentSize);
            if (!renderers.Contains(_indentGuides)) renderers.Add(_indentGuides);
        }
        else if (_indentGuides is not null)
        {
            renderers.Remove(_indentGuides);
        }
        editor.TextArea.TextView.InvalidateVisual();
    }

    /// <summary>
    /// trims trailing whitespace on every line and ensures a final newline, editing the
    /// document in place so the caret position is preserved. No-op unless the feature is enabled.
    /// </summary>
    public void ApplySaveCleanup()
    {
        if (_editor is null) return;

        // optional format-on-save (runs before the trim; itself gated on the flag).
        if (CurrentSettings.EditorFormatOnSave) FormatDocument();

        if (!EditorFeatureFlags.IsEnabled(EditorFeature.TrimTrailingOnSave,
                (_settings ?? TryGetSettings())?.Current))
            return;

        var doc = _editor.Document;
        doc.BeginUpdate();
        try
        {
            // Trim trailing spaces/tabs, last line first so earlier offsets stay valid.
            for (int i = doc.LineCount; i >= 1; i--)
            {
                var line = doc.GetLineByNumber(i);
                int end = line.EndOffset;
                int p = end;
                while (p > line.Offset && doc.GetCharAt(p - 1) is ' ' or '\t') p--;
                if (p < end) doc.Remove(p, end - p);
            }
            // Ensure the file ends with a newline.
            if (doc.TextLength > 0 && doc.GetCharAt(doc.TextLength - 1) is not ('\n' or '\r'))
                doc.Insert(doc.TextLength, NewlineFor(doc));
        }
        finally { doc.EndUpdate(); }
    }

    private static string NewlineFor(TextDocument doc)
    {
        if (doc.LineCount > 1)
            return doc.GetLineByNumber(1).DelimiterLength == 2 ? "\r\n" : "\n";
        return Environment.NewLine;
    }

    private bool IsDark() => ActualThemeVariant == ThemeVariant.Dark;

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        _colorizer?.SetVariant(IsDark() ? TherionColorizer.Variant.Dark : TherionColorizer.Variant.Light);
        _editor?.TextArea.TextView.Redraw();
    }

    // Custom syntax colors changed in Preferences (#2) — repaint with the new brushes.
    private void OnCustomColorsChanged(object? sender, EventArgs e) =>
        _editor?.TextArea.TextView.Redraw();

    // Push user edits back to the bindable Text property so a TwoWay binding
    // flushes them to the document model (which re-parses).
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        SetCurrentValue(TextProperty, _editor.Text);
        UpdateFoldings();
        UpdateBlockPairs();
        UpdateThconfigDecorations();
        RecomputeFileWarnings();
        _overviewRuler?.InvalidateVisual();
    }

    // cache the opener↔closer line pairs whenever the text changes (cheap, line-based),
    // but only while one of the consuming features is active.
    private void UpdateBlockPairs()
    {
        if (_editor?.Document is not { } doc) return;
        var s = (_settings ?? TryGetSettings())?.Current;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.MatchTerminator, s) &&
            !EditorFeatureFlags.IsEnabled(EditorFeature.SmartEnter, s))
        {
            if (_blockPairs.Count > 0) _blockPairs = new Dictionary<int, int>();
            return;
        }
        _blockPairs = TherionBlocks.BuildPairs(doc);
    }

    private void UpdateFoldings()
    {
        if (_editor?.Document is null || _foldingManager is null) return;
        var foldings = TherionFoldingStrategy.CreateFoldings(_editor.Document);
        _foldingManager.UpdateFoldings(foldings, -1);
    }

    // Files that can contain a `layout … endlayout` block (where embedded MetaPost/TeX live).
    private bool MayContainLayout()
    {
        var p = CurrentFilePath;
        if (string.IsNullOrEmpty(p)) return false;
        var ext = Path.GetExtension(p).ToLowerInvariant();
        return ext is ".thconfig" or ".thc" or ".th";
    }

    // classify each line's embedded-language region (Therion layout option / MetaPost / TeX)
    // so the colorizer highlights layout bodies with the right lexer instead of blanking them.
    private void UpdateThconfigDecorations()
    {
        if (_editor?.Document is not { } doc || _colorizer is null) return;
        _colorizer.SetLineRegions(MayContainLayout()
            ? LayoutRegionScanner.Scan(DocumentLineTexts(doc))
            : null);
        _editor.TextArea.TextView.Redraw();
    }

    private static IReadOnlyList<string> DocumentLineTexts(TextDocument doc)
    {
        var texts = new List<string>(doc.LineCount);
        foreach (var line in doc.Lines) texts.Add(doc.GetText(line));
        return texts;
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

        // Select(start, 0) resets BOTH the selection anchor and the caret to startOffset,
        // preventing AvaloniaEdit from extending selection on the next click (#2/#6).
        _editor.Select(startOffset, 0);
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

    /// <summary>When false, syntax highlighting + hover/link features are disabled (large files, #10).</summary>
    public bool HighlightingEnabled
    {
        get => GetValue(HighlightingEnabledProperty);
        set => SetValue(HighlightingEnabledProperty, value);
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
                UpdateBlockPairs();
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
            _bookmarkHighlighter?.SetContext(_bookmarks, path);
            _bookmarkMargin?.SetContext(_bookmarks, path);
            UpdateThconfigDecorations();
            RecomputeFileWarnings();
        }
        else if (change.Property == HighlightingEnabledProperty)
        {
            ApplyHighlightingEnabled(change.GetNewValue<bool>());
        }
    }

    // Large-file guard (#10): when disabled, drop the syntax colorizer so a huge document
    // doesn't pay the per-line tokenization cost; hover/link features are also gated on the flag.
    private void ApplyHighlightingEnabled(bool enabled)
    {
        if (_editor is null || _colorizer is null) return;
        var transformers = _editor.TextArea.TextView.LineTransformers;
        if (enabled)
        {
            if (!transformers.Contains(_colorizer)) transformers.Insert(0, _colorizer);
        }
        else
        {
            transformers.Remove(_colorizer);
        }
        _editor.TextArea.TextView.Redraw();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null) return;
        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        bool alt = (e.KeyModifiers & KeyModifiers.Alt) != 0;

        if (e.Key == Key.F2)
        {
            StartRename();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemCloseBrackets) // go to matching survey↔endsurvey
        {
            GoToMatchingBlock();
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Down && GatedReadingNav()) // step into the include under the caret
        {
            FollowIncludeUnderCaret();
            e.Handled = true;
        }
        else if (alt && e.Key == Key.Up && GatedReadingNav()) // step back to the including file
        {
            StepOutRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (alt && e.Key == Key.F12) // peek definition inline
        {
            PeekDefinition();
            e.Handled = true;
        }
        else if (e.Key == Key.F12 && shift)
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
        else if (shift && alt && e.Key == Key.F) // Format Document
        {
            FormatDocument();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemPeriod) // quick-fixes / code actions
        {
            ShowQuickFixes();
            e.Handled = true;
        }
    }

    /// <summary>builds line-scoped quick-fixes for the caret and shows them in a flyout.</summary>
    public void ShowQuickFixes()
    {
        if (_editor?.Document is not { } doc) return;
        var fixes = TherionCodeActions.Build(doc, _boundDiagnostics, _editor.CaretOffset, CurrentFilePath);
        var flyout = new Avalonia.Controls.MenuFlyout { Placement = Avalonia.Controls.PlacementMode.Bottom };
        if (fixes.Count == 0)
        {
            flyout.Items.Add(new Avalonia.Controls.MenuItem { Header = L("Ed_NoQuickFixes"), IsEnabled = false });
        }
        else
        {
            foreach (var fix in fixes)
            {
                var apply = fix.Apply;
                var item = new Avalonia.Controls.MenuItem { Header = fix.Title };
                item.Click += (_, _) => { try { apply(); } catch { /* ignore failed fix */ } };
                flyout.Items.Add(item);
            }
        }
        flyout.ShowAt(_editor.TextArea, showAtPointer: false);
    }

    // ----- / 16 / 17: block matching, smart Enter, reading-order navigation -----

    /// <summary>Raised when the user steps back out of an included file (Alt+Up).</summary>
    public event EventHandler? StepOutRequested;

    private AppSettings CurrentSettings => (_settings ?? TryGetSettings())?.Current ?? AppSettings.Default;

    private bool GatedReadingNav() =>
        EditorFeatureFlags.IsEnabled(EditorFeature.ReadingOrderNav, CurrentSettings);

    // Tunnel-phase handler: lets Smart Enter and Tab snippet expansion
    // preempt AvaloniaEdit's default newline / indent.
    private void OnTextAreaPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool plain = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) == 0;
        if (plain && (e.Key == Key.Enter || e.Key == Key.Return) && TrySmartEnter())
            e.Handled = true;
        else if (plain && e.Key == Key.Tab && _completionWindow is null && TryExpandSnippet())
            e.Handled = true;
    }

    /// <summary>
    /// if the word immediately before the caret is a snippet trigger, replace it with the
    /// expanded template (continuation lines indented, caret at the <c>$0</c> marker).
    /// </summary>
    private bool TryExpandSnippet()
    {
        if (_editor is null || _editor.SelectionLength > 0) return false;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.Snippets, CurrentSettings)) return false;

        int caret = _editor.CaretOffset;
        var text = _editor.Text;
        int start = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        if (start == caret) return false;
        var word = text.Substring(start, caret - start);
        if (TherionSnippets.Find(word) is not { } snippet) return false;

        TherionSnippets.Insert(_editor.TextArea, start, caret - start, snippet);
        return true;
    }

    /// <summary>
    /// when Enter is pressed at the end of an as-yet-unclosed block opener
    /// (<c>survey foo</c>), insert an indented body line plus the matching <c>endX</c>, leaving the
    /// caret on the indented line. Returns false to fall through to the normal newline + auto-indent.
    /// </summary>
    private bool TrySmartEnter()
    {
        if (_editor is null || _editor.SelectionLength > 0) return false;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.SmartEnter, CurrentSettings)) return false;

        var doc = _editor.Document;
        int caret = _editor.CaretOffset;
        var line = doc.GetLineByOffset(caret);
        if (caret != line.EndOffset) return false; // only at the very end of the opener line

        var text = doc.GetText(line);
        if (!TherionBlocks.IsBlockOpenerLine(text, out var type)) return false;
        if (_blockPairs.ContainsKey(line.LineNumber)) return false; // already has a closer

        var indent = TherionBlocks.LeadingWhitespace(text);
        var unit = IndentUnit();
        var nl = NewlineFor(doc);
        var insert = nl + indent + unit + nl + indent + TherionBlocks.CloserFor(type);
        doc.Insert(caret, insert);
        _editor.CaretOffset = caret + nl.Length + indent.Length + unit.Length;
        return true;
    }

    private string IndentUnit()
    {
        var s = CurrentSettings;
        return s.ConvertTabsToSpaces ? new string(' ', Math.Max(1, s.IndentationSize)) : "\t";
    }

    /// <summary>re-indents the whole document to its block nesting (caret-preserving).</summary>
    public void FormatDocument()
    {
        if (_editor is null) return;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.FormatDocument, CurrentSettings)) return;
        TherionFormatter.Reindent(_editor.Document, IndentUnit());
    }

    /// <summary>jumps the caret to the matching opener/closer of the block keyword line.</summary>
    public void GoToMatchingBlock()
    {
        if (_editor is null) return;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.MatchTerminator, CurrentSettings)) return;
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        if (!_blockPairs.TryGetValue(line.LineNumber, out var matchLine) ||
            matchLine < 1 || matchLine > doc.LineCount) return;

        var (start, length) = FirstWordRange(doc, matchLine);
        _editor.Select(start, 0);
        _editor.ScrollToLine(matchLine);
        if (length > 0) _flash?.Flash(start, length);
        _editor.Focus();
    }

    // keep the matching-pair highlight in sync with the caret.
    private void UpdateMatchingBlockHighlight()
    {
        if (_editor is null || _matchRenderer is null) return;
        bool on = HighlightingEnabled &&
                  EditorFeatureFlags.IsEnabled(EditorFeature.MatchTerminator, CurrentSettings);

        (int, int)? a = null, b = null;
        if (on)
        {
            var doc = _editor.Document;
            var line = doc.GetLineByOffset(_editor.CaretOffset);
            if (_blockPairs.TryGetValue(line.LineNumber, out var matchLine) &&
                matchLine >= 1 && matchLine <= doc.LineCount)
            {
                a = FirstWordRange(doc, line.LineNumber);
                b = FirstWordRange(doc, matchLine);
            }
        }
        if (_matchRenderer.Set(a, b)) _editor.TextArea.TextView.InvalidateVisual();
    }

    private static (int Start, int Length) FirstWordRange(TextDocument doc, int lineNumber)
    {
        var line = doc.GetLineByNumber(lineNumber);
        var text = doc.GetText(line);
        int lead = TherionBlocks.LeadingWhitespace(text).Length;
        int len = TherionBlocks.FirstWord(text).Length;
        return (line.Offset + lead, len);
    }

    /// <summary>opens the file referenced by the input/load/source command on the caret line.</summary>
    public void FollowIncludeUnderCaret()
    {
        if (_editor is null) return;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.ReadingOrderNav, CurrentSettings)) return;
        var doc = _editor.Document;
        var line = doc.GetLineByOffset(_editor.CaretOffset);
        var lineText = doc.GetText(line);
        var keyword = TherionBlocks.FirstWord(lineText).ToLowerInvariant();
        if (keyword is not ("input" or "load" or "source")) return;

        int lead = TherionBlocks.LeadingWhitespace(lineText).Length;
        int argCol = lead + keyword.Length;
        while (argCol < lineText.Length && char.IsWhiteSpace(lineText[argCol])) argCol++;
        if (argCol >= lineText.Length) return;
        if (ResolvePathLinkAt(line.Offset + argCol) is { } link) OpenLink(link);
    }

    /// <summary>faint highlight of the matching block opener/closer keyword pair.</summary>
    private sealed class MatchingBlockRenderer : IBackgroundRenderer
    {
        private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x40, 0x4C, 0xAF, 0x50));
        private static readonly IPen Stroke = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0x4C, 0xAF, 0x50)), 1);

        private (int Start, int Length)? _a;
        private (int Start, int Length)? _b;

        public KnownLayer Layer => KnownLayer.Selection;

        /// <summary>Sets the two highlighted ranges; returns true when they changed.</summary>
        public bool Set((int, int)? a, (int, int)? b)
        {
            if (Nullable.Equals(a, _a) && Nullable.Equals(b, _b)) return false;
            _a = a; _b = b;
            return true;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            Paint(textView, drawingContext, _a);
            Paint(textView, drawingContext, _b);
        }

        private static void Paint(TextView textView, DrawingContext dc, (int Start, int Length)? range)
        {
            if (range is not { } r || r.Length <= 0) return;
            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = r.Start, Length = r.Length });
            if (builder.CreateGeometry() is { } geometry)
                dc.DrawGeometry(Fill, Stroke, geometry);
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

    // a completion item's semantic kind (drives the glyph + description).
    private enum CompletionKind { Command, Flag, DataStyle, DataField, Type, Station, Identifier, Snippet }

    private readonly record struct CompletionCandidate(
        string Text, CompletionKind Kind, string? Insert = null, string? Description = null, bool IsSnippet = false);

    private static readonly HashSet<string> KeywordSet =
        new(TokenClassifier.Keywords, StringComparer.OrdinalIgnoreCase);

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

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = candidates
            .Where(c => prefix.Length == 0 || c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(c => seen.Add(c.Text))
            .OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();
        if (matches.Count == 0) return;

        bool rich = EditorFeatureFlags.IsEnabled(EditorFeature.RichCompletion, CurrentSettings);
        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = start,
            EndOffset = caret,
        };
        foreach (var c in matches)
            window.CompletionList.CompletionData.Add(new TherionCompletionData(c, rich));
        window.Closed += (_, _) => _completionWindow = null;
        _completionWindow = window;
        HideSignatureHelp(); // don't overlap the completion popup
        window.Show();
    }

    /// <summary>Picks the completion vocabulary (with kinds) based on what precedes the caret.</summary>
    private IReadOnlyList<CompletionCandidate> CandidateTerms(int wordStart)
    {
        if (_editor is null) return Array.Empty<CompletionCandidate>();
        var line = _editor.Document.GetLineByOffset(wordStart);
        var before = _editor.Document.GetText(line.Offset, wordStart - line.Offset);
        var tokens = before.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // First word on the line → command keywords (+ snippet triggers when is on).
        if (tokens.Length == 0)
            return FirstWordCandidates();

        switch (tokens[0].ToLowerInvariant())
        {
            case "flags":
                return Kinded(FlagValues, CompletionKind.Flag);
            case "data":
                // 2nd token is the style (normal/diving/…); the rest are field names.
                return tokens.Length == 1
                    ? Kinded(DataStyles, CompletionKind.DataStyle)
                    : Kinded(DataFields, CompletionKind.DataField);
            case "point" when IsTh2() && tokens.Length == 3:   // point x y <type>
                return Kinded(Th2Symbols.PointTypeNames, CompletionKind.Type);
            case "line" when IsTh2() && tokens.Length == 1:    // line <type>
                return Kinded(Th2Symbols.LineTypeNames, CompletionKind.Type);
            case "area" when IsTh2() && tokens.Length == 1:    // area <type>
                return Kinded(Th2Symbols.AreaTypeNames, CompletionKind.Type);
            case "input":
            case "load":
            case "encoding":
                return Array.Empty<CompletionCandidate>(); // file paths / charsets — nothing to suggest
            default:
                return IdentifierCandidates();
        }
    }

    private static IReadOnlyList<CompletionCandidate> Kinded(IEnumerable<string> terms, CompletionKind kind) =>
        terms.Select(t => new CompletionCandidate(t, kind)).ToList();

    private IReadOnlyList<CompletionCandidate> FirstWordCandidates()
    {
        var list = TokenClassifier.Keywords
            .Select(k => new CompletionCandidate(k, CompletionKind.Command, Description: CommandDocs.GetValueOrDefault(k)))
            .ToList();
        if (EditorFeatureFlags.IsEnabled(EditorFeature.Snippets, CurrentSettings))
            foreach (var s in TherionSnippets.All)
                list.Add(new CompletionCandidate(s.Trigger, CompletionKind.Snippet, Insert: s.Trigger,
                    Description: $"Snippet: {s.Description}", IsSnippet: true));
        return list;
    }

    private IReadOnlyList<CompletionCandidate> IdentifierCandidates()
    {
        var terms = CompletionTerms ?? Array.Empty<string>();
        return terms.Select(t => new CompletionCandidate(t,
            KeywordSet.Contains(t) ? CompletionKind.Command : CompletionKind.Identifier,
            Description: CommandDocs.GetValueOrDefault(t))).ToList();
    }

    private bool IsTh2() =>
        CurrentFilePath is { } p && p.EndsWith(".th2", StringComparison.OrdinalIgnoreCase);

    // ----- : parameter / signature help -----------------------------

    private Avalonia.Controls.Primitives.Popup? _sigPopup;
    private string? _sigKey;

    private static readonly Dictionary<string, string[]> CommandSignatures =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fix"]     = new[] { "station", "x/east", "y/north", "z/alt", "[sdx]", "[sdy]", "[sdz]" },
            ["equate"]  = new[] { "station", "station", "…" },
            ["cs"]      = new[] { "coordinate-system" },
            ["station"] = new[] { "name", "\"comment\"", "[flags]" },
            ["point"]   = new[] { "x", "y", "type", "[-options]" },
            ["line"]    = new[] { "type", "[-options]" },
            ["scrap"]   = new[] { "id", "[-projection]", "[-scale]", "[-options]" },
            ["map"]     = new[] { "id", "[-options]" },
        };

    // Refreshes the signature popup for the caret's command/data-row context (or hides it).
    private void UpdateSignatureHelp()
    {
        if (_editor is null) return;
        bool on = HighlightingEnabled && _completionWindow is null &&
                  EditorFeatureFlags.IsEnabled(EditorFeature.SignatureHelp, CurrentSettings);
        if (!on || ComputeSignature() is not { } sig) { HideSignatureHelp(); return; }

        EnsureSignaturePopup();
        var key = $"{sig.Title}|{sig.Active}|{string.Join(' ', sig.Params)}";
        if (key != _sigKey)
        {
            _sigKey = key;
            _sigPopup!.Child = BuildSignatureContent(sig.Title, sig.Params, sig.Active);
        }
        PositionSignaturePopup();
        if (!_sigPopup!.IsOpen) _sigPopup.IsOpen = true;
    }

    private void HideSignatureHelp()
    {
        _sigKey = null;
        if (_sigPopup is { IsOpen: true }) _sigPopup.IsOpen = false;
    }

    private (string Title, string[] Params, int Active)? ComputeSignature()
    {
        if (_editor is null) return null;
        var doc = _editor.Document;
        int caret = _editor.CaretOffset;
        var line = doc.GetLineByOffset(caret);
        var lineText = doc.GetText(line);
        int caretCol = Math.Min(caret - line.Offset, lineText.Length);
        var beforeCaret = lineText.Substring(0, caretCol);
        var fw = TherionBlocks.FirstWord(lineText).ToLowerInvariant();

        if (CommandSignatures.TryGetValue(fw, out var sig))
            return (fw, sig, Math.Clamp(ArgIndex(beforeCaret, skipFirst: true), 0, sig.Length - 1));

        if (fw.Length > 0 && !KeywordSet.Contains(fw) && NearestDataFields(line.LineNumber) is { Length: > 0 } fields)
            return ("data", fields, Math.Clamp(ArgIndex(beforeCaret, skipFirst: false), 0, fields.Length - 1));

        return null;
    }

    private static int ArgIndex(string beforeCaret, bool skipFirst)
    {
        var toks = beforeCaret.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool endsWithSpace = beforeCaret.Length > 0 && char.IsWhiteSpace(beforeCaret[^1]);
        int idx = endsWithSpace ? toks.Length : toks.Length - 1;
        if (skipFirst) idx -= 1;
        return Math.Max(0, idx);
    }

    // Nearest preceding `data <style> <fields…>` reading order (the columns of the current shot row).
    private string[]? NearestDataFields(int lineNumber)
    {
        var doc = _editor!.Document;
        for (int i = lineNumber; i >= 1; i--)
        {
            var t = doc.GetText(doc.GetLineByNumber(i));
            var w = TherionBlocks.FirstWord(t);
            if (w.Length == 0) continue;
            if (string.Equals(w, "data", StringComparison.OrdinalIgnoreCase))
            {
                var parts = t.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 2 ? parts.Skip(2).ToArray() : Array.Empty<string>();
            }
            if (TherionBlocks.CloserType(w) == "centreline") return null; // left the data section
        }
        return null;
    }

    private Control BuildSignatureContent(string title, string[] ps, int active)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        panel.Children.Add(new TextBlock { Text = title + ":", Foreground = Brushes.Gray });
        for (int i = 0; i < ps.Length; i++)
        {
            bool act = i == active;
            panel.Children.Add(new TextBlock
            {
                Text = ps[i],
                FontWeight = act ? FontWeight.Bold : FontWeight.Normal,
                Foreground = act ? (IsDark() ? Brushes.Gold : Brushes.SteelBlue) : (IBrush?)null,
            });
        }
        return new Border
        {
            Background = IsDark() ? Brushes.Black : Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3),
            Child = panel,
        };
    }

    private void EnsureSignaturePopup()
    {
        if (_sigPopup is not null) return;
        _sigPopup = new Avalonia.Controls.Primitives.Popup
        {
            PlacementTarget = _editor,
            Placement = PlacementMode.TopEdgeAlignedLeft,
            IsLightDismissEnabled = false,
        };
        if (this.FindControl<Grid>("Root") is { } root) root.Children.Add(_sigPopup);
    }

    private void PositionSignaturePopup()
    {
        if (_editor is null || _sigPopup is null) return;
        var view = _editor.TextArea.TextView;
        var loc = _editor.Document.GetLocation(_editor.CaretOffset);
        var vpos = view.GetVisualPosition(new TextViewPosition(loc), VisualYPosition.LineTop);
        if (view.TranslatePoint(vpos - view.ScrollOffset, _editor) is { } p)
        {
            _sigPopup.HorizontalOffset = p.X;
            _sigPopup.VerticalOffset = p.Y - 26; // sit just above the caret line
        }
    }

    // ----- : breadcrumb bar (enclosing survey/centreline/scrap path) -----

    private void UpdateBreadcrumb()
    {
        if (_editor is null) return;
        var bar = this.FindControl<Border>("BreadcrumbBar");
        var panel = this.FindControl<StackPanel>("Breadcrumb");
        if (bar is null || panel is null) return;

        if (!EditorFeatureFlags.IsEnabled(EditorFeature.StickyScroll, CurrentSettings))
        {
            if (bar.IsVisible) { bar.IsVisible = false; panel.Children.Clear(); }
            return;
        }

        var doc = _editor.Document;
        var path = EnclosingBlocks(doc, doc.GetLineByOffset(_editor.CaretOffset).LineNumber);
        if (path.Count == 0) { bar.IsVisible = false; panel.Children.Clear(); return; }

        panel.Children.Clear();
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = "›", Foreground = Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0),
                });
            int target = path[i].Line;
            var btn = new Button
            {
                Content = path[i].Label,
                Padding = new Thickness(5, 1),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 11,
            };
            btn.Click += (_, _) => ScrollTo(target, 1);
            panel.Children.Add(btn);
        }
        bar.IsVisible = true;
    }

    private static List<(string Label, int Line)> EnclosingBlocks(TextDocument doc, int caretLine)
    {
        var stack = new List<(string Label, int Line, string Type)>();
        int max = Math.Min(caretLine, doc.LineCount);
        for (int i = 1; i <= max; i++)
        {
            var text = doc.GetText(doc.GetLineByNumber(i));
            var fw = TherionBlocks.FirstWord(text);
            if (fw.Length == 0) continue;
            if (TherionBlocks.IsBlockOpenerLine(text, out var type))
                stack.Add((BlockLabel(type, text), i, type));
            else if (TherionBlocks.CloserType(fw) is { } ctype)
                for (int k = stack.Count - 1; k >= 0; k--)
                    if (stack[k].Type == ctype) { stack.RemoveRange(k, stack.Count - k); break; }
        }
        return stack.ConvertAll(s => (s.Label, s.Line));
    }

    private static string BlockLabel(string type, string lineText)
    {
        var parts = lineText.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && !parts[1].StartsWith('-') ? $"{type} {parts[1]}" : type;
    }

    // ----- : peek definition (inline popup) -------------------------

    private Avalonia.Controls.Primitives.Popup? _peekPopup;

    /// <summary>shows the definition of the symbol under the caret inline, without navigating.</summary>
    public void PeekDefinition()
    {
        if (_editor is null || Navigation is null) return;
        if (!EditorFeatureFlags.IsEnabled(EditorFeature.PeekDefinition, CurrentSettings)) return;
        if (ExtractRefToken(_editor.Text, _editor.CaretOffset) is not { } tok) return;
        var kind = ChooseReferenceKind(tok, _editor.CaretOffset);
        var span = Navigation.GoToDefinition(tok.Raw, kind);
        if (span is null || span.Value.IsEmpty) return;
        ShowPeek(tok.Raw, span.Value);
    }

    private void ShowPeek(string symbol, SourceSpan span)
    {
        var ctx = ReadPeekContext(span, out int defLine, out int firstLine);
        if (ctx is null) return;

        var lines = new StackPanel();
        for (int i = 0; i < ctx.Length; i++)
        {
            int lineNo = firstLine + i;
            lines.Children.Add(new TextBlock
            {
                Text = $"{lineNo,5}  {ctx[i]}",
                FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
                FontSize = 12,
                Background = lineNo == defLine ? new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xC1, 0x07)) : null,
            });
        }

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(new TextBlock
        {
            Text = $"{System.IO.Path.GetFileName(span.FilePath)} : {span.Start.Line}  —  {symbol}",
            FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center,
        });
        var openBtn = new Button { Content = L("Ed_OpenArrow"), Padding = new Thickness(6, 1), FontSize = 11 };
        openBtn.Click += (_, _) => { HidePeek(); NavigateToSpan(span); };
        header.Children.Add(openBtn);

        var border = new Border
        {
            Background = IsDark() ? Brushes.Black : Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            MaxWidth = 680,
            Child = new StackPanel { Spacing = 4, Children = { header, lines } },
        };

        EnsurePeekPopup();
        _peekPopup!.Child = border;
        PositionPeekPopup();
        _peekPopup.IsOpen = true;
    }

    private string[]? ReadPeekContext(SourceSpan span, out int defLine, out int firstLine)
    {
        const int Count = 7;
        defLine = Math.Max(1, span.Start.Line);
        firstLine = Math.Max(1, defLine - 2);
        try
        {
            if (string.Equals(span.FilePath, CurrentFilePath, StringComparison.OrdinalIgnoreCase) && _editor is not null)
            {
                var doc = _editor.Document;
                int last = Math.Min(doc.LineCount, firstLine + Count - 1);
                var list = new List<string>();
                for (int i = firstLine; i <= last; i++) list.Add(doc.GetText(doc.GetLineByNumber(i)));
                return list.Count > 0 ? list.ToArray() : null;
            }
            if (!System.IO.File.Exists(span.FilePath)) return null;
            var all = System.IO.File.ReadAllLines(span.FilePath);
            if (all.Length == 0) return null;
            firstLine = Math.Max(1, Math.Min(defLine - 2, all.Length));
            int end = Math.Min(all.Length, firstLine + Count - 1);
            var res = new List<string>();
            for (int i = firstLine; i <= end; i++) res.Add(all[i - 1]);
            return res.ToArray();
        }
        catch { return null; }
    }

    private void EnsurePeekPopup()
    {
        if (_peekPopup is not null) return;
        _peekPopup = new Avalonia.Controls.Primitives.Popup
        {
            PlacementTarget = _editor,
            Placement = PlacementMode.TopEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        if (this.FindControl<Grid>("Root") is { } root) root.Children.Add(_peekPopup);
    }

    private void PositionPeekPopup()
    {
        if (_editor is null || _peekPopup is null) return;
        var view = _editor.TextArea.TextView;
        var loc = _editor.Document.GetLocation(_editor.CaretOffset);
        var vpos = view.GetVisualPosition(new TextViewPosition(loc), VisualYPosition.LineBottom);
        if (view.TranslatePoint(vpos - view.ScrollOffset, _editor) is { } p)
        {
            _peekPopup.HorizontalOffset = Math.Max(0, p.X);
            _peekPopup.VerticalOffset = p.Y + 4; // just below the caret line
        }
    }

    private void HidePeek()
    {
        if (_peekPopup is { IsOpen: true }) _peekPopup.IsOpen = false;
    }

    // ----- rename symbol -------------------------------------------------

    /// <summary>Fires <see cref="RenameSymbolRequested"/> for the ref token at the caret.</summary>
    public void StartRename()
    {
        if (_editor is null) return;
        var tok = ExtractRefToken(_editor.Text, _editor.CaretOffset);
        if (tok is not { } t || string.IsNullOrEmpty(t.Raw)) return;
        var kind = ChooseReferenceKind(t, _editor.CaretOffset);
        RenameSymbolRequested?.Invoke(this, (t.Raw, kind));
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

        // Flag this as term navigation so the caret move it triggers is kept out of
        // the back/forward history (#1).
        IsTermNavigating = true;
        try
        {
            _editor.CaretOffset = _editor.Document.GetOffset(ln, Math.Max(1, span.Start.Column));
            _editor.ScrollToLine(ln);
        }
        finally { IsTermNavigating = false; }
        _editor.Focus();
    }

    private async void ShowGoToLine()
    {
        if (_editor is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var box = new TextBox { PlaceholderText = L("Ed_LineNumberWm"), Width = 200 };
        var dialog = new Window
        {
            Title = L("Ed_GoToLineTitle"),
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
        if (!HighlightingEnabled) return; // hover/link features off for large files (#10)
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
            HideHoverInfo();
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
            ScheduleHoverInfo(visualPos);
        }
    }

    // ----- rich hover overlay (command/identifier info, #4) --------------

    private void ScheduleHoverInfo(Point visualPos)
    {
        // While the pointer is over the open overlay, keep it exactly as is — never react to
        // whatever token sits geometrically behind the popup (which would swap or close the
        // popout the user is reaching for, item #1).
        if (_pointerOverPopup) { _infoHideTimer?.Stop(); _infoShowTimer?.Stop(); return; }

        _infoPointer = visualPos;
        int start = HoverInfoTokenStart(visualPos);
        if (start == _infoTokenStart) return; // same token — leave the popup as is

        _infoTokenStart = start;
        _infoShowTimer ??= CreateTimer(350, ShowHoverInfo);
        _infoShowTimer.Stop();
        if (start < 0)
        {
            // Left the token: don't slam the popup shut — give the user a grace window to
            // reach it (e.g. to click "Go to definition"). The hide is cancelled the moment
            // the pointer enters the popup; if it's already over the popup, keep it open (#5).
            if (!_pointerOverPopup) StartHoverHide();
            return;
        }
        _infoShowTimer.Start();
    }

    /// <summary>Document offset of an info-bearing token (command keyword / reference / path) under the pointer, or -1.</summary>
    private int HoverInfoTokenStart(Point visualPos)
    {
        if (_editor is null || FloorOffset(visualPos) is not { } off) return -1;
        if (HoverCommandWord(off) is not null) return WordStart(off);
        if (RefTokenUnderPointer(visualPos) is { } tok &&
            (Navigation?.Describe(tok.Raw, ChooseReferenceKind((tok.Start, tok.Length, tok.Raw), tok.Offset)) is not null))
            return tok.Start;
        if (ResolvePathLinkAt(off) is { } link &&
            PointerWithinRange(_editor.TextArea.TextView, link.Start, link.Start + link.Length, visualPos.X))
            return link.Start;
        return -1;
    }

    private void ShowHoverInfo()
    {
        _infoShowTimer?.Stop();
        if (_editor is null || FloorOffset(_infoPointer) is not { } off) return;

        Control? content =
            HoverCommandWord(off) is { } cmd ? BuildCommandInfo(cmd) :
            RefTokenUnderPointer(_infoPointer) is { } tok &&
                Navigation?.Describe(tok.Raw, ChooseReferenceKind((tok.Start, tok.Length, tok.Raw), tok.Offset)) is { } info
                ? BuildIdentifierInfo(tok.Raw, info) :
            ResolvePathLinkAt(off) is { } link ? BuildFileInfo(link) : null;

        if (content is null) { CancelHoverInfo(); return; }

        EnsureInfoPopup();
        var border = new Border
        {
            Background = IsDark() ? Brushes.Black : Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            MaxWidth = 460,
            Child = content,
        };
        // Enter/exit MUST be wired on the popup content (the Border), not the Popup control:
        // a Popup hosts its child in a separate PopupRoot, so pointer events fire on the
        // content, not the Popup. Keeping the popup open while the pointer is over it is what
        // lets the user reach its "Go to definition" / "Find all references" buttons (#1).
        border.PointerEntered += OnHoverContentEntered;
        border.PointerExited += OnHoverContentExited;
        _infoPopup!.Child = border;
        _infoPopup.IsOpen = true;
        // Return focus to the editor so the popup doesn't intercept keyboard and
        // hover events (#3).
        _editor.TextArea.Focus();
    }

    // The pointer moved onto the hover overlay → keep it open (cancel the grace-hide and any
    // pending show for a token geometrically behind the popup).
    private void OnHoverContentEntered(object? sender, PointerEventArgs e)
    {
        _pointerOverPopup = true;
        _infoHideTimer?.Stop();
        _infoShowTimer?.Stop();
    }

    // The pointer left the overlay → start the grace-hide. It is cancelled again if the
    // pointer re-enters the overlay or settles back on the same token.
    private void OnHoverContentExited(object? sender, PointerEventArgs e)
    {
        _pointerOverPopup = false;
        StartHoverHide();
    }

    // Fully cancel hover: stops the show timer and closes the popup.
    // Call this when the mouse leaves all token areas or a diagnostic appears.
    private void CancelHoverInfo()
    {
        _infoShowTimer?.Stop();
        DismissHoverPopup();
    }

    // Close the popup without cancelling the pending show timer.
    // Called from the popup's exit-hide timer so a new token's popup can still open (#3).
    private void DismissHoverPopup()
    {
        _infoTokenStart = -1;
        _pointerOverPopup = false;
        if (_infoPopup is { IsOpen: true }) _infoPopup.IsOpen = false;
    }

    // Keep HideHoverInfo as an alias so all existing call-sites that mean "full cancel" still work.
    private void HideHoverInfo() => CancelHoverInfo();

    private void EnsureInfoPopup()
    {
        if (_infoPopup is not null) return;
        _infoPopup = new Avalonia.Controls.Primitives.Popup
        {
            PlacementTarget = _editor,
            Placement = PlacementMode.Pointer,
            // Light dismiss would lay an input-catching overlay over the window that swallows
            // the editor's hover detection; we drive show/hide entirely from pointer enter/exit
            // instead, so it stays off (#1).
            IsLightDismissEnabled = false,
            HorizontalOffset = 8,
            VerticalOffset = 16,
        };
        // Backstop the content-level handlers in case a host renders the popup as an overlay
        // within this window (where the Popup control itself sees the pointer).
        _infoPopup.PointerEntered += OnHoverContentEntered;
        _infoPopup.PointerExited += OnHoverContentExited;
        // Route all window pointer moves through the editor's hover detection while
        // the popup is open so tokens geometrically behind it remain discoverable (#3).
        _infoPopup.Opened += OnInfoPopupOpened;
        _infoPopup.Closed += OnInfoPopupClosed;
        if (this.FindControl<Grid>("Root") is { } root) root.Children.Add(_infoPopup);
    }

    private void OnInfoPopupOpened(object? sender, EventArgs e)
    {
        _windowForPopup = TopLevel.GetTopLevel(this);
        _windowForPopup?.AddHandler(InputElement.PointerMovedEvent, OnWindowPointerMovedForPopup,
            RoutingStrategies.Tunnel);
    }

    private void OnInfoPopupClosed(object? sender, EventArgs e)
    {
        _windowForPopup?.RemoveHandler(InputElement.PointerMovedEvent, OnWindowPointerMovedForPopup);
        _windowForPopup = null;
    }

    // Called in tunnel phase on the main window while the hover popup is open.
    // Converts window-relative coordinates to TextView coordinates so the editor's
    // hover detection keeps working even when the mouse is over the popup overlay (#3).
    private void OnWindowPointerMovedForPopup(object? sender, PointerEventArgs e)
    {
        if (_windowForPopup is null || _editor?.TextArea.TextView is not { } tv) return;
        var windowPos = e.GetPosition(_windowForPopup);
        // tv is in the window's visual tree — TranslatePoint resolves correctly.
        if (_windowForPopup.TranslatePoint(windowPos, tv) is { } tvPos)
            ScheduleHoverInfo(tvPos + tv.ScrollOffset);
    }

    private void StartHoverHide()
    {
        // Use DismissHoverPopup (not CancelHoverInfo/HideHoverInfo) so that a pending
        // show timer for a different token is NOT cancelled when the popup auto-hides (#3).
        if (_infoHideTimer is null)
        {
            _infoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _infoHideTimer.Tick += (_, _) => { _infoHideTimer!.Stop(); DismissHoverPopup(); };
        }
        _infoHideTimer.Stop();
        _infoHideTimer.Start();
    }

    private DispatcherTimer CreateTimer(int ms, Action tick)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); tick(); };
        return t;
    }

    private string? HoverCommandWord(int offset)
    {
        if (_editor is null) return null;
        var line = _editor.Document.GetLineByOffset(offset);
        var text = _editor.Document.GetText(line);
        int lead = 0; while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
        int end = lead; while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        int col = offset - line.Offset;
        if (col < lead || col >= end) return null; // not over the first word
        var word = text[lead..end];
        return CommandDocs.ContainsKey(word) ? word : null;
    }

    private int WordStart(int offset)
    {
        var line = _editor!.Document.GetLineByOffset(offset);
        var text = _editor.Document.GetText(line);
        int lead = 0; while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
        return line.Offset + lead;
    }

    private Control BuildCommandInfo(string command)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = command, FontWeight = FontWeight.Bold, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = CommandDocs[command], TextWrapping = TextWrapping.Wrap });
        var doc = new Button { Content = L("Ed_Documentation"), Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Left };
        doc.Click += (_, _) => { OpenDocumentation(command); HideHoverInfo(); };
        panel.Children.Add(doc);
        return panel;
    }

    private static IThbookDocumentationService? TryDocs()
    {
        try { return AppServices.Provider.GetService<IThbookDocumentationService>(); }
        catch { return null; } // design-time / no container
    }

    private static IDocumentService? TryDocuments()
    {
        try { return AppServices.Provider.GetService<IDocumentService>(); }
        catch { return null; }
    }

    // "Open in 3D" is offered for station/survey references when the embedded viewer is enabled
    // and actually has a model to show. Stations highlight in place; surveys get a bounding box —
    // both via the same selection path (IDocumentService.RequestShowInModel3D → SelectInModel).
    private static bool Model3DAvailableFor(string? kind)
    {
        if (kind is not ("station" or "survey")) return false;
        try
        {
            if (AppServices.Provider.GetService<IAppSettingsService>()?.Current.EnableModel3DViewer != true)
                return false;
            return AppServices.Provider.GetService<ViewModels.Model3DViewerViewModel>()?.CanShowInViewer == true;
        }
        catch { return false; }
    }

    private void OpenInModel3D(string name)
    {
        if (!string.IsNullOrWhiteSpace(name)) TryDocuments()?.RequestShowInModel3D(name);
        HideHoverInfo();
    }

    /// <summary>
    /// Opens the bundled thbook PDF at the page mapped to <paramref name="term"/> (#6),
    /// falling back to the online wiki when there's no mapping or the PDF can't be opened.
    /// </summary>
    private void OpenDocumentation(string term)
    {
        if (TryDocs() is { } svc && svc.Open(term)) return;
        OpenExternalRequested?.Invoke(this, ThbookUrl);
    }

    private Control BuildIdentifierInfo(string raw, ReferenceInfo info)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = raw, FontWeight = FontWeight.Bold, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = $"{info.Kind}", Foreground = Brushes.Gray });

        var where = info.Declaration;
        if (!string.IsNullOrEmpty(where.FilePath))
        {
            var link = new TextBlock
            {
                Text = $"{System.IO.Path.GetFileName(where.FilePath)} : {where.Start.Line}",
                Foreground = Brushes.SteelBlue,
                Cursor = _handCursor,
                TextDecorations = TextDecorations.Underline,
            };
            link.PointerPressed += (_, _) => { NavigateToSpan(where); HideHoverInfo(); };
            panel.Children.Add(link);
        }

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var go = new Button { Content = L("Ed_GoToDefinition"), Padding = new Thickness(6, 2) };
        go.Click += (_, _) => { NavigateToSpan(where); HideHoverInfo(); };
        var refs = new Button { Content = L("Ed_FindAllRefs"), Padding = new Thickness(6, 2) };
        refs.Click += (_, _) => { FindReferencesRequested?.Invoke(this, StationRef.Parse(raw).PointWithoutMark); HideHoverInfo(); };
        var renameBtn = new Button { Content = L("Ed_RenameDots"), Padding = new Thickness(6, 2) };
        renameBtn.Click += (_, _) =>
        {
            HideHoverInfo();
            // Rename opens a modal dialog. Closing this hover popup (which hosts the button handling the
            // click) and opening the modal in the same input turn corrupts pointer routing — the leftover
            // click reaches the torn-down popup ("PlatformImpl is null") and the dialog chain stalls. Defer
            // the rename to a Background-priority tick so the popup teardown + click fully drain first.
            Avalonia.Threading.Dispatcher.UIThread.Post(StartRename, Avalonia.Threading.DispatcherPriority.Background);
        };
        actions.Children.Add(go);
        actions.Children.Add(refs);
        actions.Children.Add(renameBtn);

        // "Open in 3D" — only for stations/surveys, and only when a 3D model is available.
        if (Model3DAvailableFor(info.Kind))
        {
            var open3d = new Button { Content = L("Ed_OpenIn3D"), Padding = new Thickness(6, 2) };
            open3d.Click += (_, _) => OpenInModel3D(raw);
            actions.Children.Add(open3d);
        }

        // Documentation button: links to the thbook page for this reference kind (#6),
        // shown only when a page mapping exists for it (info.Kind is "station"/"survey"/…).
        if (info.Kind is { Length: > 0 } docTerm && TryDocs() is { } svc && svc.TryGetPage(docTerm, out _))
        {
            var docBtn = new Button { Content = L("Ed_Documentation"), Padding = new Thickness(6, 2) };
            docBtn.Click += (_, _) => { OpenDocumentation(docTerm); HideHoverInfo(); };
            actions.Children.Add(docBtn);
        }

        panel.Children.Add(actions);
        return panel;
    }

    private Control BuildFileInfo(PathLink link)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = link.Raw, FontWeight = FontWeight.Bold, FontSize = 14 });
        panel.Children.Add(new TextBlock
        {
            Text = link.ResolvedExisting is null ? "file (not found)" : "file",
            Foreground = Brushes.Gray,
        });
        var open = new Button
        {
            Content = link.ShellOpen ? L("Ed_OpenDefaultApp") : L("Ed_OpenFile"),
            Padding = new Thickness(6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = link.ResolvedExisting is not null,
        };
        open.Click += (_, _) => { OpenLink(link); HideHoverInfo(); };
        panel.Children.Add(open);
        return panel;
    }

    private void ClearHover()
    {
        if (_editor is null || _hoverDiagnostic is null) return;
        _hoverDiagnostic = null;
        ToolTip.SetIsOpen(_editor, false);
    }

    // ----- occurrence highlight + caret reporting ------------------------

    /// <summary>raised by the focused editor with the current selection's character and
    /// line counts (0,0 when the selection is empty). The shell shows these in the status bar.</summary>
    public static event EventHandler<(int Chars, int Lines)>? SelectionStatsChanged;

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        int chars = _editor.SelectionLength;
        if (chars <= 0) { SelectionStatsChanged?.Invoke(this, (0, 0)); return; }
        var text = _editor.SelectedText;
        int lines = 1;
        foreach (var c in text) if (c == '\n') lines++;
        SelectionStatsChanged?.Invoke(this, (chars, lines));
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_editor is null) return;

        // Dim-highlight every occurrence of the identifier under the caret/selection (#10).
        if (_occurrences?.SetWord(SelectedOrCaretWord()) == true)
            _editor.TextArea.TextView.InvalidateVisual();

        // highlight the matching block opener/closer when the caret sits on a block keyword.
        UpdateMatchingBlockHighlight();
        // track the active parameter as the caret moves across a command/data row.
        UpdateSignatureHelp();
        // refresh the enclosing-block breadcrumb. : dismiss any open peek.
        UpdateBreadcrumb();
        HidePeek();

        // Report the caret location for the navigation history (#1/#8). Consume the
        // pointer-press flag so this single move is attributed to the click that caused it.
        if (!string.IsNullOrEmpty(CurrentFilePath))
        {
            LastCaretMoveFromPointer = _caretFromPointer;
            _caretFromPointer = false;
            LastCaretMoveFromSearch = IsSearchPanelFocused();
            var loc = _editor.Document.GetLocation(_editor.CaretOffset);
            CaretMoved?.Invoke(this, new SourceSpan(
                CurrentFilePath!, new SourceLocation(loc.Line, loc.Column),
                new SourceLocation(loc.Line, loc.Column), _editor.CaretOffset, 0));
        }
    }

    // Marks the imminent caret move as pointer-driven (left click only), for history (#8).
    private void OnEditorPointerPressedCapture(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is not null && e.GetCurrentPoint(_editor).Properties.IsLeftButtonPressed)
            _caretFromPointer = true;
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

    // Collapses any selection AvaloniaEdit created between anchor (definition) and
    // mouse-up position after a navigation click fires on PointerPressed (#2).
    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _caretFromPointer = false; // drop an unconsumed flag (click that didn't move the caret)
        if (_pendingNavOffset < 0 || _editor is null) return;
        _editor.Select(_pendingNavOffset, 0);
        _pendingNavOffset = -1;
    }

    // ----- right-click: move caret to the click before showing the menu -------

    private void OnEditorContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_editor is null) return;
        // refresh the gated items' visibility for the current settings.
        var fs = CurrentSettings;
        if (_matchMenuItem is not null)
            _matchMenuItem.IsVisible = EditorFeatureFlags.IsEnabled(EditorFeature.MatchTerminator, fs);
        if (_stepIntoMenuItem is not null)
            _stepIntoMenuItem.IsVisible = EditorFeatureFlags.IsEnabled(EditorFeature.ReadingOrderNav, fs);
        if (_formatMenuItem is not null)
            _formatMenuItem.IsVisible = EditorFeatureFlags.IsEnabled(EditorFeature.FormatDocument, fs);
        if (_peekMenuItem is not null)
            _peekMenuItem.IsVisible = EditorFeatureFlags.IsEnabled(EditorFeature.PeekDefinition, fs) && Navigation is not null;
        if (_open3dMenuItem is not null) _open3dMenuItem.IsVisible = false;   // hidden unless on a station/survey
        _open3dName = null;

        var view = _editor.TextArea.TextView;
        // Keyboard-invoked menus (Menu key) carry no pointer position — leave the caret put.
        if (!e.TryGetPosition(view, out var p)) return;
        if (FloorOffset(p + view.ScrollOffset) is not { } off) return;

        UpdateOpen3dMenuItem(p + view.ScrollOffset);

        // Right-clicking inside an existing selection keeps it (so Cut/Copy act on the
        // selection); clicking elsewhere moves the caret there and clears any selection.
        if (_editor.SelectionLength > 0)
        {
            int selStart = _editor.SelectionStart;
            if (off >= selStart && off <= selStart + _editor.SelectionLength) return;
        }
        _editor.Select(off, 0);
    }

    // #5: enable the "Open in 3D" item when the token under the pointer is a station/survey
    // reference and the embedded viewer has a model to show.
    private void UpdateOpen3dMenuItem(Point visualPos)
    {
        if (_open3dMenuItem is null) return;
        if (RefTokenUnderPointer(visualPos) is { } tok &&
            Navigation?.Describe(tok.Raw, ChooseReferenceKind((tok.Start, tok.Length, tok.Raw), tok.Offset)) is { } info &&
            Model3DAvailableFor(info.Kind))
        {
            _open3dName = tok.Raw;
            _open3dMenuItem.IsVisible = true;
        }
    }

    // ----- go to definition / linked files -------------------------------

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editor is null) return;
        if (!HighlightingEnabled) return; // go-to-definition/link navigation off for large files (#10)
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
                // Store the definition offset so OnEditorPointerReleased can collapse
                // any selection AvaloniaEdit extends between anchor and mouse position (#2).
                if (_editor?.Document is { } anchorDoc &&
                    string.Equals(span.FilePath, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    var defLine = Math.Max(1, span.Start.Line);
                    if (defLine <= anchorDoc.LineCount)
                        _pendingNavOffset = anchorDoc.GetOffset(defLine, Math.Max(1, span.Start.Column));
                }
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

    /// <summary>Public wrapper so callers outside the editor can resolve ref tokens (e.g. rename-change search).</summary>
    public static (int Start, int Length, string Raw)? ExtractRefTokenStatic(string text, int offset)
        => ExtractRefToken(text, offset);

    /// <summary>Extracts the reference token (id, <c>@</c>, survey path) surrounding <paramref name="offset"/>.</summary>
    private static (int Start, int Length, string Raw)? ExtractRefToken(string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length) return null;
        int start = offset;
        while (start > 0 && IsRefChar(text[start - 1])) start--;
        int end = offset;
        while (end < text.Length && IsRefChar(text[end])) end++;
        if (end <= start) return null;
        var raw = text.Substring(start, end - start);
        // "." is Therion's splay placeholder (no destination station) — not a real ref (#5).
        if (raw == ".") return null;
        return (start, end - start, raw);
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

    // a completion item that shows a per-kind glyph + a documentation/detail tooltip
    // (when rich), and — for snippet triggers — expands its template on accept.
    private sealed class TherionCompletionData : ICompletionData
    {
        private readonly CompletionKind _kind;
        private readonly string _insert;
        private readonly string? _description;
        private readonly bool _rich;
        private readonly bool _isSnippet;

        public TherionCompletionData(string text) // plain (fallback)
        {
            Text = text;
            _insert = text;
            _kind = CompletionKind.Identifier;
            _rich = false;
        }

        public TherionCompletionData(CompletionCandidate c, bool rich)
        {
            Text = c.Text;
            _insert = c.Insert ?? c.Text;
            _kind = c.Kind;
            _description = c.Description;
            _rich = rich;
            _isSnippet = c.IsSnippet;
        }

        public IImage? Image => null;
        public string Text { get; }
        public object Content => _rich ? $"{GlyphFor(_kind)}  {Text}" : Text;
        public object Description => _description ?? KindLabel(_kind);
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            if (_isSnippet && TherionSnippets.Find(Text) is { } snippet)
                TherionSnippets.Insert(textArea, completionSegment.Offset, completionSegment.Length, snippet);
            else
                textArea.Document.Replace(completionSegment, _insert);
        }

        private static string GlyphFor(CompletionKind kind) => kind switch
        {
            CompletionKind.Command   => "⌘",
            CompletionKind.Flag      => "⚑",
            CompletionKind.DataStyle => "≣",
            CompletionKind.DataField => "▤",
            CompletionKind.Type      => "◈",
            CompletionKind.Station   => "•",
            CompletionKind.Snippet   => "✂",
            _                        => "·",
        };

        private static string KindLabel(CompletionKind kind) => kind switch
        {
            CompletionKind.Command   => "Command",
            CompletionKind.Flag      => "Flag value",
            CompletionKind.DataStyle => "Data style",
            CompletionKind.DataField => "Data reading",
            CompletionKind.Type      => "Symbol type",
            CompletionKind.Station   => "Station",
            CompletionKind.Snippet   => "Snippet",
            _                        => "Identifier",
        };
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

    /// <summary>
    /// draws faint dashed vertical guides at each indentation stop on the visible lines,
    /// making nested survey / centreline / scrap blocks easier to scan.
    /// </summary>
    private sealed class IndentGuideRenderer : IBackgroundRenderer
    {
        private static readonly IPen GuidePen =
            new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)), 1,
                    dashStyle: new DashStyle(new double[] { 1, 3 }, 0));

        private int _indentSize = 2;

        public void SetIndentSize(int n) => _indentSize = Math.Max(1, n);

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document is not { } doc || textView.VisualLines.Count == 0) return;
            double spaceWidth = textView.WideSpaceWidth;
            if (spaceWidth <= 0) return;
            double step = _indentSize * spaceWidth;

            foreach (var vl in textView.VisualLines)
            {
                int cols = LeadingIndentColumns(doc, vl.FirstDocumentLine, _indentSize);
                int levels = cols / _indentSize;
                // Level 0 sits in the margin; a single-level indent has no ancestor guide to draw.
                if (levels < 2) continue;

                double yTop = vl.VisualTop - textView.VerticalOffset;
                double yBottom = yTop + vl.Height;
                for (int k = 1; k < levels; k++)
                {
                    double x = Math.Round(k * step) - textView.HorizontalOffset + 0.5;
                    if (x < 0) continue;
                    drawingContext.DrawLine(GuidePen, new Point(x, yTop), new Point(x, yBottom));
                }
            }
        }

        private static int LeadingIndentColumns(TextDocument doc, DocumentLine line, int indentSize)
        {
            int col = 0;
            for (int offset = line.Offset; offset < line.EndOffset; offset++)
            {
                char c = doc.GetCharAt(offset);
                if (c == ' ') col++;
                else if (c == '\t') col += indentSize - (col % indentSize);
                else break;
            }
            return col;
        }
    }

    /// <summary>draws a small colour swatch beside layout <c>color</c> values and <c>#hex</c> literals.</summary>
    private sealed class ColorAdornmentRenderer : IBackgroundRenderer
    {
        private static readonly IPen SwatchPen = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x80, 0x80, 0x80)), 1);
        private static readonly System.Text.RegularExpressions.Regex HexRegex =
            new(@"#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b", System.Text.RegularExpressions.RegexOptions.Compiled);

        public bool Enabled { get; set; } = true;
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView view, DrawingContext dc)
        {
            if (!Enabled || view.Document is not { } doc || view.VisualLines.Count == 0) return;
            double size = Math.Max(8, view.DefaultLineHeight - 6);
            foreach (var vl in view.VisualLines)
            {
                var line = vl.FirstDocumentLine;
                if (FindColor(doc.GetText(line)) is not { } hit) continue;
                int endOffset = Math.Min(line.Offset + hit.End, doc.TextLength);
                var vpos = view.GetVisualPosition(new TextViewPosition(doc.GetLocation(endOffset)), VisualYPosition.TextTop);
                double x = vpos.X - view.ScrollOffset.X + 4;
                double y = vpos.Y - view.ScrollOffset.Y + 2;
                if (x < 0 || y < 0) continue;
                dc.DrawRectangle(new SolidColorBrush(hit.Color), SwatchPen, new RoundedRect(new Rect(x, y, size, size), 2));
            }
        }

        private static (int End, Color Color)? FindColor(string text)
        {
            if (string.Equals(TherionBlocks.FirstWord(text), "color", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = text.TrimEnd();
                string token;
                if (trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    int open = trimmed.LastIndexOf('[');
                    token = open >= 0 ? trimmed.Substring(open) : trimmed;
                }
                else
                {
                    int sep = Math.Max(trimmed.LastIndexOf(' '), trimmed.LastIndexOf('\t'));
                    token = trimmed.Substring(sep + 1);
                }
                if (TryParseColor(token, out var c)) return (trimmed.Length, c);
            }
            var m = HexRegex.Match(text);
            return m.Success && TryParseHex(m.Value, out var hc) ? (m.Index + m.Length, hc) : null;
        }

        private static bool TryParseColor(string token, out Color color)
        {
            color = default;
            token = token.Trim();
            if (token.Length == 0) return false;
            if (token[0] == '#') return TryParseHex(token, out color);
            if (token[0] == '[' && token[^1] == ']')
            {
                var inner = token.Substring(1, token.Length - 2)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (inner.Length == 3 && Num(inner[0], out var r) && Num(inner[1], out var g) && Num(inner[2], out var b))
                { color = Color.FromRgb(Scale(r), Scale(g), Scale(b)); return true; }
                if (inner.Length == 1 && Num(inner[0], out var gray))
                { var v = Scale(gray); color = Color.FromRgb(v, v, v); return true; }
                return false;
            }
            if (Num(token, out var grayVal)) { var v = Scale(grayVal); color = Color.FromRgb(v, v, v); return true; }
            return false;
        }

        private static bool TryParseHex(string token, out Color color)
        {
            color = default;
            var h = token.TrimStart('#');
            if (h.Length == 3) h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
            if (h.Length != 6) return false;
            try
            {
                color = Color.FromRgb(
                    Convert.ToByte(h.Substring(0, 2), 16),
                    Convert.ToByte(h.Substring(2, 2), 16),
                    Convert.ToByte(h.Substring(4, 2), 16));
                return true;
            }
            catch { return false; }
        }

        private static bool Num(string s, out double v) =>
            double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v);

        // Therion colour components are 0–100 (percent); map to 0–255.
        private static byte Scale(double pct) => (byte)Math.Clamp(Math.Round(pct * 2.55), 0, 255);
    }

    // ----- context menu (B2) ---------------------------------------------

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeItem(L("Menu_Edit_Cut"),       CutSelection));
        menu.Items.Add(MakeItem(L("Menu_Edit_Copy"),      CopySelection));
        menu.Items.Add(MakeItem(L("Menu_Edit_Paste"),     () => _ = PasteAsync()));
        menu.Items.Add(MakeItem(L("Menu_Edit_Delete"),    DeleteSelection));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(L("Menu_Edit_SelectAll"), () => _editor?.SelectAll()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(L("Menu_Edit_Upper"),     () => ApplyCase(upper: true)));
        menu.Items.Add(MakeItem(L("Menu_Edit_Lower"),     () => ApplyCase(upper: false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(L("Ed_ToggleComment"),    ToggleLineComment));
        menu.Items.Add(new Separator());
        // line operations.
        menu.Items.Add(MakeItem(L("Ed_DuplicateLines"),   DuplicateLines));
        menu.Items.Add(MakeItem(L("Ed_MoveLinesUp"),      MoveLinesUp));
        menu.Items.Add(MakeItem(L("Ed_MoveLinesDown"),    MoveLinesDown));
        menu.Items.Add(MakeItem(L("Ed_SortLines"),        SortSelectedLines));
        menu.Items.Add(new Separator());
        // insert helpers.
        menu.Items.Add(MakeItem(L("Ed_InsertDate"),       InsertDate));
        menu.Items.Add(MakeItem(L("Ed_InsertTeamMember"), InsertTeamMember));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(L("Ed_RenameSymbol"),     StartRename));
        // #5: shown only when right-clicking a station/survey reference and the 3D viewer has a model.
        _open3dMenuItem = MakeItem(L("Ed_OpenIn3D"), () => { if (_open3dName is { } n) OpenInModel3D(n); });
        menu.Items.Add(_open3dMenuItem);
        menu.Items.Add(new Separator());
        // shown only when their feature is enabled (refreshed on menu open).
        _matchMenuItem = MakeItem(L("Ed_GoToMatching"), GoToMatchingBlock);
        _stepIntoMenuItem = MakeItem(L("Ed_StepInto"), FollowIncludeUnderCaret);
        _formatMenuItem = MakeItem(L("Ed_FormatDoc"), FormatDocument);
        _peekMenuItem = MakeItem(L("Ed_PeekDef"), PeekDefinition);
        menu.Items.Add(_matchMenuItem);
        menu.Items.Add(_stepIntoMenuItem);
        menu.Items.Add(_peekMenuItem);
        menu.Items.Add(_formatMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(L("Menu_Edit_FoldAll"),   () => SetAllFoldings(folded: true)));
        menu.Items.Add(MakeItem(L("Menu_Edit_UnfoldAll"), () => SetAllFoldings(folded: false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(L("Menu_Edit_AddBookmark"), () => _ = AddBookmarkAsync()));
        return menu;
    }

    // ----- public menu surface (shell Edit/Search menus mirror the context menu, #11/#12) ----

    /// <summary>The current editor selection (empty when nothing is selected); seeds Find/Replace.</summary>
    public string SelectedText => _editor?.SelectedText ?? string.Empty;

    public void MenuCut() => CutSelection();
    public void MenuCopy() => CopySelection();
    public void MenuPaste() => _ = PasteAsync();
    public void MenuDelete() => DeleteSelection();
    public void MenuSelectAll() => _editor?.SelectAll();
    public void MenuUpperCase() => ApplyCase(upper: true);
    public void MenuLowerCase() => ApplyCase(upper: false);
    public void MenuToggleComment() => ToggleLineComment();
    public void MenuFoldAll() => SetAllFoldings(folded: true);
    public void MenuUnfoldAll() => SetAllFoldings(folded: false);
    public void MenuAddBookmark() => _ = AddBookmarkAsync();

    /// <summary>Opens the in-document search panel (Find, #12).</summary>
    public void MenuFind()
    {
        if (_searchPanel is null) return;
        _searchPanel.IsReplaceMode = false;
        _searchPanel.Open();
        if (_editor?.SelectionLength > 0) _searchPanel.SearchPattern = _editor.SelectedText;
    }

    /// <summary>Opens the Go-to-Line dialog (command palette, #4).</summary>
    public void MenuGoToLine() => ShowGoToLine();

    // ----- : drop a workspace file → insert an input/source line ----

    private void OnEditorDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer is { } dt && dt.Contains(InsertPathFormat))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;   // claim it so it isn't treated as an open-file drop
        }
    }

    private void OnEditorDrop(object? sender, DragEventArgs e)
    {
        if (_editor is null || e.DataTransfer is not { } dt) return;
        if (dt.TryGetValue(InsertPathFormat) is not { Length: > 0 } path) return;

        var keyword = IsThconfigPath(CurrentFilePath) ? "source" : "input";
        var line = $"{keyword} \"{RelativeToCurrent(path)}\"";

        int offset = _editor.CaretOffset;
        try
        {
            if (_editor.GetPositionFromPoint(e.GetPosition(_editor)) is { } pos)
                offset = _editor.Document.GetOffset(pos.Location);
        }
        catch { /* fall back to the caret offset */ }

        var doc = _editor.Document;
        var docLine = doc.GetLineByOffset(offset);
        doc.Insert(docLine.Offset, line + NewlineFor(doc));
        e.Handled = true;
    }

    private static bool IsThconfigPath(string? path)
    {
        var ext = System.IO.Path.GetExtension(path ?? string.Empty);
        return ext.Equals(".thconfig", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".thc", StringComparison.OrdinalIgnoreCase);
    }

    private string RelativeToCurrent(string path)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(CurrentFilePath);
            var rel = string.IsNullOrEmpty(dir) ? System.IO.Path.GetFileName(path) : System.IO.Path.GetRelativePath(dir, path);
            return rel.Replace('\\', '/');
        }
        catch { return System.IO.Path.GetFileName(path); }
    }

    /// <summary>true when keyboard focus is inside the in-editor search panel (so its
    /// find-next caret moves can be kept out of the navigation history).</summary>
    private bool IsSearchPanelFocused()
    {
        if (_searchPanel is null) return false;
        var top = TopLevel.GetTopLevel(this);
        if (top?.FocusManager?.GetFocusedElement() is not Visual v) return false;
        for (Visual? cur = v; cur is not null; cur = Avalonia.VisualTree.VisualExtensions.GetVisualParent(cur))
            if (ReferenceEquals(cur, _searchPanel)) return true;
        return false;
    }

    /// <summary>Opens the in-document search panel in replace mode (Replace, #12).</summary>
    public void MenuReplace()
    {
        if (_searchPanel is null) return;
        _searchPanel.IsReplaceMode = true;
        _searchPanel.Open();
        if (_editor?.SelectionLength > 0) _searchPanel.SearchPattern = _editor.SelectedText;
    }

    /// <summary>Go to line: scrolls/positions the caret at <paramref name="line"/> (1-based) (#12).</summary>
    public void GoToLine(int line)
    {
        if (_editor is null) return;
        line = Math.Clamp(line, 1, _editor.Document.LineCount);
        ScrollTo(line, 1);
        _editor.TextArea.Focus();
    }

    public int LineCount => _editor?.Document.LineCount ?? 0;
    /// <summary>The current editor selection text (empty when nothing is selected) (#10).</summary>
    public string EditorSelectedText => _editor?.SelectedText ?? string.Empty;

    public int CurrentLine => _editor?.TextArea.Caret.Line ?? 1;

    // Collapses/expands every foldable region in the document (#8).
    private void SetAllFoldings(bool folded)
    {
        if (_foldingManager is null) return;
        foreach (var folding in _foldingManager.AllFoldings)
            folding.IsFolded = folded;
        _editor?.TextArea.TextView.Redraw();
    }

    // ----- line operations --------------------------------------
    // Duplicate, move up/down and sort the selected lines (or the caret line). Useful for
    // hand-editing centreline `data` blocks. Lines in the affected region are rejoined with the
    // document's newline (Therion files use a uniform EOL).

    /// <summary>Duplicates the selected lines (or the caret line) directly below.</summary>
    public void DuplicateLines()
    {
        if (_editor is null) return;
        var doc = _editor.Document;
        var (first, last) = SelectedLineRange();
        int from = first.Offset, to = last.EndOffset;
        var block = doc.GetText(from, to - from);
        doc.Insert(to, NewlineFor(doc) + block);
    }

    /// <summary>Moves the selected lines (or the caret line) up by one.</summary>
    public void MoveLinesUp() => MoveLines(-1);
    /// <summary>Moves the selected lines (or the caret line) down by one.</summary>
    public void MoveLinesDown() => MoveLines(+1);

    private void MoveLines(int dir)
    {
        if (_editor is null) return;
        var doc = _editor.Document;
        var (first, last) = SelectedLineRange();
        var neighbour = dir < 0 ? first.PreviousLine : last.NextLine;
        if (neighbour is null) return;

        var regionFirst = dir < 0 ? neighbour : first;
        var regionLast = dir < 0 ? last : neighbour;
        int regionStart = regionFirst.Offset, regionEnd = regionLast.EndOffset;

        var lines = new System.Collections.Generic.List<string>();
        for (var l = regionFirst; l is not null; l = l.NextLine)
        {
            lines.Add(doc.GetText(l.Offset, l.Length));
            if (l == regionLast) break;
        }

        string nl = NewlineFor(doc);
        int blockCount = last.LineNumber - first.LineNumber + 1;
        if (dir < 0) { var moved = lines[0]; lines.RemoveAt(0); lines.Add(moved); }
        else { var moved = lines[^1]; lines.RemoveAt(lines.Count - 1); lines.Insert(0, moved); }

        var replacement = string.Join(nl, lines);
        doc.Replace(regionStart, regionEnd - regionStart, replacement);

        // Re-select the moved block in its new position.
        int newStart = dir < 0 ? regionStart : regionStart + lines[0].Length + nl.Length;
        var blockLines = lines.GetRange(dir < 0 ? 0 : 1, blockCount);
        int blockLen = string.Join(nl, blockLines).Length;
        _editor.Select(newStart, blockLen);
        _editor.CaretOffset = newStart + blockLen;
    }

    /// <summary>Sorts the selected lines alphabetically (case-insensitive).</summary>
    public void SortSelectedLines()
    {
        if (_editor is null || _editor.SelectionLength == 0) return;
        var doc = _editor.Document;
        var (first, last) = SelectedLineRange();
        var lines = new System.Collections.Generic.List<string>();
        for (var l = first; l is not null; l = l.NextLine)
        {
            lines.Add(doc.GetText(l.Offset, l.Length));
            if (l == last) break;
        }
        lines.Sort(StringComparer.OrdinalIgnoreCase);
        int start = first.Offset, end = last.EndOffset;
        doc.Replace(start, end - start, string.Join(NewlineFor(doc), lines));
    }

    /// <summary>The first/last document lines touched by the selection (or the caret line).</summary>
    private (AvaloniaEdit.Document.DocumentLine First, AvaloniaEdit.Document.DocumentLine Last) SelectedLineRange()
    {
        var doc = _editor!.Document;
        bool hasSel = _editor.SelectionLength > 0;
        int s = hasSel ? _editor.SelectionStart : _editor.CaretOffset;
        int e = hasSel ? _editor.SelectionStart + _editor.SelectionLength : _editor.CaretOffset;
        return (doc.GetLineByOffset(s), doc.GetLineByOffset(e));
    }

    // ----- insert helpers ---------------------------------------

    /// <summary>Inserts today's date in Therion's <c>yyyy.mm.dd</c> format at the caret.</summary>
    public void InsertDate()
    {
        if (_editor is null) return;
        ReplaceSelectionOrInsert(DateTime.Now.ToString("yyyy.MM.dd"));
    }

    /// <summary>Inserts a <c>team ""</c> line template, leaving the caret between the quotes.</summary>
    public void InsertTeamMember()
    {
        if (_editor is null) return;
        const string tpl = "team \"\"";
        int caret = _editor.CaretOffset;
        _editor.Document.Insert(caret, tpl);
        _editor.CaretOffset = caret + tpl.Length - 1;   // between the quotes
        _editor.TextArea.Focus();
    }

    private void ReplaceSelectionOrInsert(string text)
    {
        if (_editor is null) return;
        if (_editor.SelectionLength > 0)
            _editor.Document.Replace(_editor.SelectionStart, _editor.SelectionLength, text);
        else
            _editor.Document.Insert(_editor.CaretOffset, text);
    }

    /// <summary>
    /// The range a context action targets: the selection when there is one, otherwise the
    /// whole current line. <paramref name="includeDelimiter"/> adds the line's newline so
    /// cut/delete remove the entire line (like VS Code's no-selection line cut, #3).
    /// </summary>
    private (int Start, int Length) ActionRange(bool includeDelimiter)
    {
        if (_editor is null) return (0, 0);
        if (_editor.SelectionLength > 0)
            return (_editor.SelectionStart, _editor.SelectionLength);
        var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
        return (line.Offset, includeDelimiter ? line.TotalLength : line.Length);
    }

    // Localized string lookup (Strings.resx / .ro.resx) for code-built menus & dialogs.
    private static string L(string key) => TherionProc.Resources.Tr.Get(key);

    private static MenuItem MakeItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    // Access clipboard via the same ApplicationLifetime path used elsewhere in the project.
    private static void TrySetClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            _ = life.MainWindow?.Clipboard?.SetTextAsync(text);
    }

    private static async System.Threading.Tasks.Task<string?> TryGetClipboardAsync()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life &&
            life.MainWindow?.Clipboard is { } cb)
            return await cb.TryGetTextAsync();
        return null;
    }

    private void CutSelection()
    {
        if (_editor is null) return;
        var (start, length) = ActionRange(includeDelimiter: true);
        if (length == 0) return;
        TrySetClipboard(_editor.Document.GetText(start, length));
        _editor.Document.Remove(start, length);
    }

    private void CopySelection()
    {
        if (_editor is null) return;
        var (start, length) = ActionRange(includeDelimiter: true);
        if (length == 0) return;
        TrySetClipboard(_editor.Document.GetText(start, length));
    }

    private async System.Threading.Tasks.Task PasteAsync()
    {
        if (_editor is null) return;
        var text = await TryGetClipboardAsync();
        if (text is null) return;
        _editor.Document.BeginUpdate();
        try
        {
            if (_editor.SelectionLength > 0)
                _editor.Document.Replace(_editor.SelectionStart, _editor.SelectionLength, text);
            else
                _editor.Document.Insert(_editor.CaretOffset, text);
        }
        finally { _editor.Document.EndUpdate(); }
    }

    private void DeleteSelection()
    {
        if (_editor is null) return;
        var (start, length) = ActionRange(includeDelimiter: true);
        if (length == 0) return;
        _editor.Document.Remove(start, length);
    }

    private void ApplyCase(bool upper)
    {
        if (_editor is null) return;
        // Case ops keep the newline out of range so the line break isn't disturbed.
        var (start, length) = ActionRange(includeDelimiter: false);
        if (length == 0) return;
        var src = _editor.Document.GetText(start, length);
        var text = upper ? src.ToUpperInvariant() : src.ToLowerInvariant();
        _editor.Document.Replace(start, length, text);
    }

    // ----- bookmark add (B3) ---------------------------------------------

    private async System.Threading.Tasks.Task AddBookmarkAsync()
    {
        if (_editor is null || string.IsNullOrEmpty(CurrentFilePath)) return;
        int line = _editor.Document.GetLineByOffset(_editor.CaretOffset).LineNumber;

        var box = new TextBox { PlaceholderText = L("Ed_BookmarkTitleWm"), Width = 280, Margin = new Thickness(0, 0, 0, 4) };
        var ok = new Button { Content = L("Ed_AddBtn"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = L("Ed_AddBookmarkTitle"),
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Spacing = 8,
                Children = { box, ok },
            },
        };
        ok.Click += (_, _) => dialog.Close(box.Text ?? string.Empty);
        box.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter) dialog.Close(box.Text ?? string.Empty);
            else if (ke.Key == Key.Escape) dialog.Close(null);
        };
        box.Loaded += (_, _) => box.Focus();

        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var title = await dialog.ShowDialog<string?>(owner);
        if (title is null) return; // cancelled

        try
        {
            var svc = _bookmarks ?? AppServices.Provider.GetService<IBookmarksService>();
            svc?.AddBookmark(CurrentFilePath!, line, title);
        }
        catch { }
    }

    // ----- bookmark highlight renderer (B3) ------------------------------

    private sealed class BookmarkHighlightRenderer : IBackgroundRenderer
    {
        private static readonly IBrush Fill =
            new SolidColorBrush(Color.FromArgb(0x55, 0x20, 0x80, 0xC8));

        private readonly TextView _view;
        private IBookmarksService? _service;
        private string? _filePath;

        public BookmarkHighlightRenderer(TextView view) => _view = view;

        public KnownLayer Layer => KnownLayer.Background;

        public void SetContext(IBookmarksService? service, string? filePath)
        {
            _service = service;
            _filePath = filePath;
            _view.InvalidateVisual();
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (_service is null || string.IsNullOrEmpty(_filePath)) return;
            if (textView.Document is null || textView.VisualLines.Count == 0) return;

            var bookmarkedLines = new System.Collections.Generic.HashSet<int>();
            foreach (var b in _service.Bookmarks)
                if (string.Equals(b.FilePath, _filePath, StringComparison.OrdinalIgnoreCase))
                    bookmarkedLines.Add(b.Line);
            if (bookmarkedLines.Count == 0) return;

            foreach (var vl in textView.VisualLines)
            {
                if (!bookmarkedLines.Contains(vl.FirstDocumentLine.LineNumber)) continue;
                var seg = new TextSegment
                {
                    StartOffset = vl.FirstDocumentLine.Offset,
                    Length      = vl.FirstDocumentLine.TotalLength,
                };
                var builder = new BackgroundGeometryBuilder { CornerRadius = 0 };
                builder.AddSegment(textView, seg);
                if (builder.CreateGeometry() is { } geom)
                    drawingContext.DrawGeometry(Fill, null, geom);
            }
        }
    }

    /// <summary>
    /// A thin left-gutter margin that draws a small coloured box next to the line number
    /// of every bookmarked line, so bookmarks are visible at the line marker too (#4).
    /// </summary>
    private sealed class BookmarkMargin : AbstractMargin
    {
        private const double MarginWidth = 13;
        private const double BoxSize = 9;
        private static readonly IBrush BoxFill = new SolidColorBrush(Color.FromRgb(0x20, 0x80, 0xC8));
        private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x50, 0x90)), 1);

        private IBookmarksService? _service;
        private string? _filePath;

        public void SetContext(IBookmarksService? service, string? filePath)
        {
            if (_service is not null) _service.BookmarksChanged -= OnBookmarksChanged;
            _service = service;
            _filePath = filePath;
            if (_service is not null) _service.BookmarksChanged += OnBookmarksChanged;
            InvalidateVisual();
        }

        private void OnBookmarksChanged(object? sender, EventArgs e) => InvalidateVisual();

        protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
            InvalidateVisual();
        }

        private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

        // right-click a bookmark in the gutter to remove it.
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (_service is null || string.IsNullOrEmpty(_filePath) || TextView is not { VisualLinesValid: true } tv) return;
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

            double y = e.GetPosition(this).Y;
            foreach (var vl in tv.VisualLines)
            {
                double top = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop) - tv.VerticalOffset;
                if (y < top || y >= top + vl.Height) continue;
                int line = vl.FirstDocumentLine.LineNumber;
                foreach (var b in _service.Bookmarks)
                    if (b.Line == line && string.Equals(b.FilePath, _filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _service.RemoveBookmark(b);
                        e.Handled = true;
                        return;
                    }
                return;
            }
        }

        public override void Render(DrawingContext context)
        {
            var tv = TextView;
            if (tv is null || !tv.VisualLinesValid || _service is null || string.IsNullOrEmpty(_filePath)) return;

            var marked = new HashSet<int>();
            foreach (var b in _service.Bookmarks)
                if (string.Equals(b.FilePath, _filePath, StringComparison.OrdinalIgnoreCase))
                    marked.Add(b.Line);
            if (marked.Count == 0) return;

            foreach (var vl in tv.VisualLines)
            {
                if (!marked.Contains(vl.FirstDocumentLine.LineNumber)) continue;
                double top = vl.GetTextLineVisualYPosition(vl.TextLines[0], VisualYPosition.TextTop) - tv.VerticalOffset;
                double y = top + (vl.TextLines[0].Height - BoxSize) / 2;
                var rect = new Rect((MarginWidth - BoxSize) / 2, y, BoxSize, BoxSize);
                context.DrawRectangle(BoxFill, BoxPen, new RoundedRect(rect, 2));
            }
        }
    }
}
