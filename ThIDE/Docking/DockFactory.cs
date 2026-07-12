// Builds and owns the VS-classic dock layout:
//   Left:   Workspace
//   Center: document well (open .th files)  +  Bottom: Diagnostics / Compiler Output / Generated Files
//   Right:  Outline / preview panels (XVI references available behind the ShowXviPanel switch)
// Also wires the locators Dock needs to materialize tools and floating windows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer.SystemTextJson;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Docking;

public sealed class DockFactory : Factory
{
    private static readonly string LayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ThIDE", "dock-layout.json");

    // Crash sentinel: created while a saved layout is being applied, deleted once the window
    // has rendered. If it's still present on the next launch, that apply crashed mid-render,
    // so we discard the layout instead of bricking startup (task 2).
    private static readonly string LoadSentinelPath = LayoutPath + ".loading";

    // The stock Dock serializer: its built-in resolver carries the polymorphic type
    // discriminators that let an IRootDock tree round-trip. (A custom resolver dropped
    // those discriminators, which made deserialization throw "Deserialization of interface
    // or abstract types is not supported" and silently reset the layout every launch.)
    // We avoid the cyclic / non-round-trippable parts of OUR view-models by serializing a
    // skeleton of plain Dock base types instead — see BuildSkeleton.
    private readonly IDockSerializer _serializer = new DockSerializer();
    private string? _lastSavedJson;
    private readonly ILogger? _logger;
    private readonly WelcomeToolViewModel _welcome;
    private readonly WorkspaceExplorerToolViewModel _workspace;
    private readonly ObjectBrowserToolViewModel _objectBrowser;
    private readonly DiagnosticsToolViewModel _diagnostics;
    private readonly CompilerOutputToolViewModel _compilerOutput;
    private readonly GeneratedFilesToolViewModel _generatedFiles;
    private readonly XviToolViewModel _xvi;
    private readonly OutlineToolViewModel _outline;
    private readonly ProjectToolViewModel _project;
    private readonly LogToolViewModel _log;
    private readonly LivePreviewToolViewModel _livePreview;
    private readonly MapViewerToolViewModel _mapViewer;
    private readonly Model3DViewerToolViewModel _model3dViewer;
    private readonly StructuralGeologyToolViewModel _structuralGeology;
    private readonly StructuralPlotToolViewModel _structuralPlot;
    private readonly BlenderAnimationToolViewModel _blenderAnimation;
    private readonly ThIDE.Services.IAppSettingsService? _appSettings;
    private readonly SettingsToolViewModel _settings;

    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;

    private readonly ThIDE.Services.ILayoutService? _layoutState;

    public DockFactory(
        WelcomeToolViewModel welcome,
        WorkspaceExplorerToolViewModel workspace,
        ObjectBrowserToolViewModel objectBrowser,
        DiagnosticsToolViewModel diagnostics,
        CompilerOutputToolViewModel compilerOutput,
        GeneratedFilesToolViewModel generatedFiles,
        XviToolViewModel xvi,
        OutlineToolViewModel outline,
        ProjectToolViewModel project,
        LogToolViewModel log,
        LivePreviewToolViewModel livePreview,
        MapViewerToolViewModel mapViewer,
        Model3DViewerToolViewModel model3dViewer,
        StructuralGeologyToolViewModel structuralGeology,
        StructuralPlotToolViewModel structuralPlot,
        BlenderAnimationToolViewModel blenderAnimation,
        SettingsToolViewModel settings,
        ThIDE.Services.ILayoutService? layoutState = null,
        ThIDE.Services.IAppSettingsService? appSettings = null,
        ILogger<DockFactory>? logger = null)
    {
        _welcome = welcome;
        _workspace = workspace;
        _objectBrowser = objectBrowser;
        _diagnostics = diagnostics;
        _compilerOutput = compilerOutput;
        _generatedFiles = generatedFiles;
        _xvi = xvi;
        _outline = outline;
        _project = project;
        _log = log;
        _livePreview = livePreview;
        _mapViewer = mapViewer;
        _model3dViewer = model3dViewer;
        _structuralGeology = structuralGeology;
        _structuralPlot = structuralPlot;
        _blenderAnimation = blenderAnimation;
        _appSettings = appSettings;
        _settings = settings;
        _layoutState = layoutState;
        _logger = logger;
    }

    /// <summary>The central document well; <see cref="OpenDocument"/> adds tabs here.</summary>
    public DocumentDock? DocumentDock => _documentDock;

    // ---- unsaved-changes guard on close --------------------------------------
    // Closing a modified document is intercepted: OnDockableClosing cancels the close and an async
    // prompt (wired by the shell via DocumentCloseGuard) offers Save / Don't save / Cancel, then
    // re-issues the close if the user agreed. Covers the tab X, the bulk "close others/all/right"
    // actions and any programmatic CloseDockable — they all funnel through OnDockableClosing.

    /// <summary>Shell-supplied prompt for a dirty document; returns true to proceed with the close.</summary>
    public Func<FileDocumentViewModel, Task<bool>>? DocumentCloseGuard { get; set; }

    // Documents whose close the user has already confirmed (self-clearing: consumed on re-close).
    private readonly HashSet<FileDocumentViewModel> _closeConfirmed = new();
    // Documents whose save-prompt is currently open, so a second click on the tab's X while the
    // dialog is up doesn't stack another prompt.
    private readonly HashSet<FileDocumentViewModel> _closePromptInFlight = new();

    public override bool OnDockableClosing(IDockable? dockable)
    {
        if (dockable is FileDocumentViewModel doc && DocumentCloseGuard is { } guard
            && doc.IsDirty && !_closeConfirmed.Remove(doc))
        {
            if (_closePromptInFlight.Add(doc)) _ = ConfirmThenCloseAsync(doc, guard);
            return false;   // cancel now; the async flow re-issues the close if the user agrees
        }
        return base.OnDockableClosing(dockable);
    }

    private async Task ConfirmThenCloseAsync(FileDocumentViewModel doc, Func<FileDocumentViewModel, Task<bool>> guard)
    {
        bool proceed;
        try { proceed = await guard(doc).ConfigureAwait(true); }
        catch { proceed = true; }   // never trap a tab open on a prompt failure
        finally { _closePromptInFlight.Remove(doc); }
        if (!proceed) return;
        _closeConfirmed.Add(doc);
        try { CloseDockable(doc); } catch { /* best-effort re-close */ }
    }

    /// <summary>
    /// Activates the Compiler Output tool: selects its tab in whatever dock holds it and, if it
    /// has been torn off into a float window, raises that window to the front (#2).
    /// </summary>
    public void ShowCompilerOutput()
    {
        if (_rootDock is null) return;
        try
        {
            SetActiveDockable(_compilerOutput);
            SetFocusedDockable(_rootDock, _compilerOutput);
            BringHostWindowToFront(_compilerOutput);
        }
        catch { /* best-effort focus */ }
    }

    /// <summary>
    /// Activates a tool, first adding it to the right tool-rail if it isn't in the layout yet.
    /// Lets feature-flagged tools (e.g. the 3D viewer, off by default) appear the moment
    /// they're enabled, without requiring a layout reset.
    /// </summary>
    public void ShowTool(IDockable tool)
    {
        if (_rootDock is null) return;
        try
        {
            if (!ContainsRef(_rootDock, tool) && FindDockById<IToolDock>(_rootDock, "RightTools") is { } right)
                AddDockable(right, tool);
            SetActiveDockable(tool);
            SetFocusedDockable(_rootDock, tool);
            BringHostWindowToFront(tool);
        }
        catch { /* best-effort show */ }
    }

    /// <summary>
    /// Activates a tool in the central document well (like the Object Browser), adding it there on
    /// demand if it isn't in the layout yet. Used for the big-panel Structural Geology view.
    /// </summary>
    public void ShowToolInDocuments(IDockable tool)
    {
        if (_rootDock is null || _documentDock is null) return;
        try
        {
            if (!ContainsRef(_rootDock, tool)) AddDockable(_documentDock, tool);
            SetActiveDockable(tool);
            SetFocusedDockable(_rootDock, tool);
            BringHostWindowToFront(tool);
        }
        catch { /* best-effort show */ }
    }

    /// <summary>If <paramref name="tool"/> lives in a float window, brings that window to front.</summary>
    private void BringHostWindowToFront(IDockable tool)
    {
        if (_rootDock?.Windows is not { } windows) return;
        foreach (var w in windows)
        {
            if (w.Layout is { } layout && ContainsRef(layout, tool) &&
                w.Host is Avalonia.Controls.Window win)
            {
                win.Activate();
                return;
            }
        }
    }

    // ---- map-viewer window controls (#7) ------------------------------------

    /// <summary>The float host window currently containing <paramref name="tool"/>, or null if docked.</summary>
    private Avalonia.Controls.Window? HostWindowOf(IDockable tool)
    {
        if (_rootDock?.Windows is not { } windows) return null;
        foreach (var w in windows)
            if (w.Layout is { } layout && ContainsRef(layout, tool) && w.Host is Avalonia.Controls.Window win)
                return win;
        return null;
    }

    /// <summary>Ensures <paramref name="tool"/> is torn off into its own float window; returns it.</summary>
    private Avalonia.Controls.Window? EnsureFloating(IDockable tool)
    {
        var win = HostWindowOf(tool);
        if (win is not null) return win;
        try { FloatDockable(tool); } catch { return null; }
        return HostWindowOf(tool);
    }

    /// <summary>Toggles a full-screen float window for <paramref name="tool"/> (#7).</summary>
    public void ToggleToolFullScreen(IDockable tool)
    {
        var win = EnsureFloating(tool);
        if (win is null) return;
        win.WindowState = win.WindowState == Avalonia.Controls.WindowState.FullScreen
            ? Avalonia.Controls.WindowState.Normal
            : Avalonia.Controls.WindowState.FullScreen;
        win.Activate();
    }

    /// <summary>
    /// Floats <paramref name="tool"/> onto a different monitor and maximizes it there (#7). Returns
    /// false when only one screen is available (the caller can notify the user).
    /// </summary>
    public bool FloatToolOnOtherScreen(IDockable tool)
    {
        var win = EnsureFloating(tool);
        if (win?.Screens is not { } screens || screens.ScreenCount < 2) return false;
        var current = screens.ScreenFromWindow(win) ?? screens.Primary;
        var other = screens.All.FirstOrDefault(s => !ReferenceEquals(s, current)) ?? screens.All.FirstOrDefault();
        if (other is null) return false;
        // Normalize first, drop the window onto the target screen, then maximize on it.
        win.WindowState = Avalonia.Controls.WindowState.Normal;
        win.Position = other.WorkingArea.Position;
        win.WindowState = Avalonia.Controls.WindowState.Maximized;
        win.Activate();
        return true;
    }

    /// <summary>Moves <paramref name="tool"/> into the central document well if it isn't already there (#7).</summary>
    public void MoveToolToDocuments(IDockable tool)
    {
        if (_rootDock is null || _documentDock is null) return;
        try
        {
            if (ReferenceEquals(tool.Owner, _documentDock)) { SetActiveDockable(tool); return; }
            if (ContainsRef(_rootDock, tool) || HostWindowOf(tool) is not null)
                RemoveDockable(tool, collapse: false);
            AddDockable(_documentDock, tool);
            SetActiveDockable(tool);
            SetFocusedDockable(_rootDock, tool);
        }
        catch { /* best-effort move */ }
    }

    // Dock-tree persistence is DISABLED: Dock.Avalonia 12 does not render content for a
    // deserialized-and-reswapped layout tree — the tab strips appear but the content controls
    // never instantiate their views, leaving every panel blank. Building the default layout
    // (which renders correctly) avoids that. Window geometry still persists via ILayoutService
    // and the open files reopen via session restore. Re-enable only once a restore strategy
    // that produces a renderable tree is in place (e.g. rebuilding rather than deserializing).
    private const bool PersistDockLayout = false;

    // The XVI references panel is hidden by default. Flip this single switch to surface it in the
    // right tool-rail again. There is no menu / command-palette / keyboard-shortcut entry for it,
    // so this constant is the only place that decides whether the panel is reachable at all.
    private const bool ShowXviPanel = false;

    public override IRootDock CreateLayout() =>
        (PersistDockLayout ? TryLoadLayout() : null) ?? TryBuildFromSavedProfile() ?? BuildDefaultLayout();

    /// <summary>Builds a fresh default layout for the "reset layout" command (#16).</summary>
    public IRootDock ResetToDefault() => BuildDefaultLayout();

    // ---- full-layout persistence via the declarative profile -----------------
    // The dock tree is never deserialized (a deserialized Dock 12 tree doesn't render);
    // instead the autosaved LayoutState carries a LayoutProfile — a plain description of
    // which tool sits in which dock section — and the tree is REBUILT from it here, using
    // the same programmatic construction as BuildDefaultLayout (which renders correctly).

    /// <summary>Rebuilds the dock tree described by the autosaved profile; null → default.
    /// Guarded by the crash sentinel: if the previous profile apply died mid-render, the
    /// profile is skipped once and the next autosave overwrites it with a healthy one.</summary>
    private IRootDock? TryBuildFromSavedProfile()
    {
        try
        {
            if (_layoutState?.Current.Profile is not { } profile) return null;
            if (File.Exists(LoadSentinelPath))
            {
                TryDelete(LoadSentinelPath);
                return null;
            }
            TryWrite(LoadSentinelPath, string.Empty);
            return BuildFromProfile(profile);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Saved layout profile could not be applied; using the default layout.");
            TryDelete(LoadSentinelPath);
            return null;
        }
    }

    /// <summary>Closes every torn-off float window (host + model). Used before swapping in a
    /// rebuilt layout so the old tree's OS windows don't linger. The dockables themselves are
    /// live singletons and survive — the new tree re-docks (or re-floats) them.</summary>
    public void CloseAllFloatWindows()
    {
        if (_rootDock?.Windows is not { } windows) return;
        foreach (var w in windows.ToList())
        {
            try { w.Exit(); } catch { /* host may already be gone */ }
            try { RemoveWindow(w); } catch { /* may have been removed by Exit */ }
        }
    }

    /// <summary>Tool ids that exist AND are enabled by their feature settings.</summary>
    public IReadOnlySet<string> AvailableToolIds()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ToolSingletonsById().Keys)
            if (IsToolAvailable(id))
                set.Add(id);
        return set;
    }

    /// <summary>Mirrors the feature gates the default layout applies (see RightToolset).</summary>
    private bool IsToolAvailable(string id)
    {
        var s = _appSettings?.Current ?? ThIDE.Services.AppSettings.Default;
        return id switch
        {
            "Xvi" => ShowXviPanel,
            "Outline" => ThIDE.Services.EditorFeatureFlags.IsEnabled(ThIDE.Services.EditorFeature.Outline, s),
            "LivePreview" => s.EnableLivePreview,
            "MapViewer" => s.EnableInAppViewer,
            "Model3DViewer" => s.EnableModel3DViewer,
            "StructuralGeology" or "StructuralPlot" => s.EnableStructuralGeology,
            "BlenderAnimation" => s.EnableBlenderAnimation,
            _ => true,
        };
    }

    // Tools the default layout shows: they must stay reachable even when a profile (saved
    // by an older version, or hand-edited) doesn't mention them — matching today's behavior
    // where every launch rebuilds the default set.
    private static readonly string[] DefaultDockToolIds =
    {
        "Workspace", "Project", "ObjectBrowser",
        "Diagnostics", "CompilerOutput", "GeneratedFiles", "Log",
        "Xvi", "Outline", "LivePreview", "MapViewer", "Model3DViewer",
    };

    /// <summary>Which region a tool calls home when the profile doesn't place it.</summary>
    private static string HomeRegion(string id) => id switch
    {
        "Workspace" or "Project" => "left",
        "Diagnostics" or "CompilerOutput" or "GeneratedFiles" or "Log" => "bottom",
        "ObjectBrowser" => "center",
        _ => "right",
    };

    // A section being assembled: live tools, the selected tab and its share of the rail.
    private sealed record BuiltSection(List<IDockable> Tools, IDockable? Active, double Proportion);

    /// <summary>Resolves a profile's section list to live tool singletons, dropping unknown /
    /// disabled / already-placed ids and empty sections.</summary>
    private List<BuiltSection> MaterializeSections(
        IEnumerable<ThIDE.Services.DockSectionState> sections,
        Dictionary<string, IDockable> map, HashSet<string> placed)
    {
        var result = new List<BuiltSection>();
        foreach (var s in sections)
        {
            var tools = new List<IDockable>();
            IDockable? active = null;
            foreach (var id in s.Tools)
            {
                if (!map.TryGetValue(id, out var tool) || !IsToolAvailable(id) || !placed.Add(id)) continue;
                tools.Add(tool);
                if (string.Equals(id, s.ActiveTool, StringComparison.Ordinal)) active = tool;
            }
            if (tools.Count > 0) result.Add(new BuiltSection(tools, active, s.Proportion));
        }
        return result;
    }

    private ToolDock MakeToolDock(string id, Alignment alignment, BuiltSection s, double proportion) => new()
    {
        Id = id,
        Title = id,
        Alignment = alignment,
        Proportion = proportion > 0 ? proportion : double.NaN,
        VisibleDockables = CreateList<IDockable>(s.Tools.ToArray()),
        ActiveDockable = s.Active ?? s.Tools.FirstOrDefault(),
    };

    /// <summary>Builds one docking region: a single ToolDock, or a stack of them separated by
    /// splitters when the profile divides the region into several sections. The first section
    /// keeps the canonical id ("LeftTools"/"RightTools"/"BottomTools") because other features
    /// look docks up by those ids.</summary>
    private IDockable BuildRegion(
        List<BuiltSection> sections, string baseId, Alignment alignment,
        double regionProportion, Orientation stack)
    {
        if (sections.Count == 0)
            return new ToolDock
            {
                Id = baseId,
                Title = baseId,
                Alignment = alignment,
                Proportion = regionProportion,
                VisibleDockables = CreateList<IDockable>(),
            };
        if (sections.Count == 1)
            return MakeToolDock(baseId, alignment, sections[0], regionProportion);

        var children = CreateList<IDockable>();
        for (int i = 0; i < sections.Count; i++)
        {
            if (i > 0) children.Add(new ProportionalDockSplitter());
            var share = sections[i].Proportion > 0 ? sections[i].Proportion : 1.0 / sections.Count;
            children.Add(MakeToolDock(i == 0 ? baseId : baseId + (i + 1), alignment, sections[i], share));
        }
        return new ProportionalDock
        {
            Id = baseId + "Rail",
            Orientation = stack,
            Proportion = regionProportion,
            VisibleDockables = children,
        };
    }

    /// <summary>
    /// Builds a renderable dock tree from a declarative profile: left/right rails (possibly
    /// split into stacked sections), bottom sections, tools docked in the document well.
    /// Tools referenced by the profile's float list are docked into their home region first —
    /// <see cref="RestoreFloatWindows"/> then tears them off, the same live operation a manual
    /// tear-off performs (which is what renders correctly in Dock 12).
    /// </summary>
    public IRootDock BuildFromProfile(ThIDE.Services.LayoutProfile p)
    {
        var map = ToolSingletonsById();
        var placed = new HashSet<string>(StringComparer.Ordinal);

        var leftSections = MaterializeSections(p.LeftSections, map, placed);
        var rightSections = MaterializeSections(p.RightSections, map, placed);
        var bottomSections = MaterializeSections(p.BottomSections, map, placed);

        var centerTools = new List<IDockable>();
        IDockable? centerActive = null;
        foreach (var id in p.CenterTools)
        {
            if (!map.TryGetValue(id, out var tool) || !IsToolAvailable(id) || !placed.Add(id)) continue;
            centerTools.Add(tool);
            if (string.Equals(id, p.CenterActiveTool, StringComparison.Ordinal)) centerActive = tool;
        }

        // Default tools the profile doesn't place + tools its float windows need: append each
        // to its home region so nothing becomes unreachable and every float can be re-created.
        var needed = new List<string>(DefaultDockToolIds);
        foreach (var w in p.FloatWindows)
            foreach (var id in w.DockableIds)
                if (map.ContainsKey(id) && !needed.Contains(id))
                    needed.Add(id);
        foreach (var id in needed)
        {
            if (placed.Contains(id) || !map.TryGetValue(id, out var tool) || !IsToolAvailable(id)) continue;
            placed.Add(id);
            switch (HomeRegion(id))
            {
                case "left": AppendToRegion(leftSections, tool); break;
                case "bottom": AppendToRegion(bottomSections, tool); break;
                case "center": centerTools.Add(tool); break;
                default: AppendToRegion(rightSections, tool); break;
            }
        }

        double leftProp = p.LeftProportion > 0 ? p.LeftProportion : 0.18;
        double rightProp = p.RightProportion > 0 ? p.RightProportion : 0.22;
        double bottomProp = p.BottomProportion > 0 ? p.BottomProportion : 0.28;
        double centerProp = p.CenterProportion > 0 ? p.CenterProportion : 0.60;

        var documentDock = NewDocumentDock();
        _documentDock = documentDock;
        documentDock.VisibleDockables = CreateList<IDockable>(centerTools.ToArray());
        documentDock.ActiveDockable = centerActive ?? centerTools.FirstOrDefault();

        var leftNode = BuildRegion(leftSections, "LeftTools", Alignment.Left, leftProp, Orientation.Vertical);
        var rightNode = BuildRegion(rightSections, "RightTools", Alignment.Right, rightProp, Orientation.Vertical);
        var bottomNode = BuildRegion(bottomSections, "BottomTools", Alignment.Bottom, bottomProp, Orientation.Horizontal);

        var centerColumn = new ProportionalDock
        {
            Id = "CenterColumn",
            Orientation = Orientation.Vertical,
            Proportion = centerProp,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter(),
                bottomNode),
        };

        var mainRow = new ProportionalDock
        {
            Id = "MainRow",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftNode,
                new ProportionalDockSplitter(),
                centerColumn,
                new ProportionalDockSplitter(),
                rightNode),
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(mainRow);
        root.ActiveDockable = mainRow;
        root.DefaultDockable = mainRow;
        _rootDock = root;
        return root;
    }

    private static void AppendToRegion(List<BuiltSection> sections, IDockable tool)
    {
        if (sections.Count == 0)
            sections.Add(new BuiltSection(new List<IDockable> { tool }, tool, 0));
        else
            sections[^1].Tools.Add(tool);
    }

    private ThIDE.Services.LayoutProfile? _lastCapturedProfile;
    private string? _lastCapturedProfileJson;

    /// <summary>Snapshots the live layout (docked arrangement + float windows) as a profile.
    /// Returns a cached instance when nothing changed so the autosave dedup keeps working.</summary>
    public ThIDE.Services.LayoutProfile CaptureProfile()
    {
        var known = ToolSingletonsById();
        var profile = _rootDock is null
            ? new ThIDE.Services.LayoutProfile()
            : ThIDE.Services.LayoutProfileCapture.Capture(_rootDock, id => known.ContainsKey(id));
        profile = profile with { FloatWindows = CaptureFloatWindows() };
        // Records holding lists compare by reference, so dedup via the serialized form.
        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        if (_lastCapturedProfile is not null &&
            string.Equals(json, _lastCapturedProfileJson, StringComparison.Ordinal))
            return _lastCapturedProfile;
        _lastCapturedProfileJson = json;
        return _lastCapturedProfile = profile;
    }

    private IDockable BottomTabById(string? id) => id switch
    {
        "CompilerOutput" => _compilerOutput,
        "GeneratedFiles" => _generatedFiles,
        "Log"            => _log,
        _                => _diagnostics,
    };

    /// <summary>
    /// Reads the live dock tree's pane proportions + active tabs into <paramref name="baseState"/>
    /// for persistence (#17). Safe to call any time; missing docks keep the base values.
    /// </summary>
    public ThIDE.Services.LayoutState CaptureLayoutState(ThIDE.Services.LayoutState baseState)
    {
        if (_rootDock is null) return baseState;
        double Prop(string id, double fallback) =>
            FindDockById<IDock>(_rootDock, id) is { Proportion: > 0 and var p } ? p : fallback;
        string? Active(string id) =>
            FindDockById<IDock>(_rootDock, id)?.ActiveDockable?.Id;
        return baseState with
        {
            LeftProportion = Prop("LeftTools", baseState.LeftProportion),
            RightProportion = Prop("RightTools", baseState.RightProportion),
            BottomProportion = Prop("BottomTools", baseState.BottomProportion),
            CenterProportion = Prop("CenterColumn", baseState.CenterProportion),
            BottomActiveTab = Active("BottomTools") ?? baseState.BottomActiveTab,
            RightActiveTab = Active("RightTools") ?? baseState.RightActiveTab,
            // capture floated windows — but never before they were restored, or an
            // autosave that fires mid-startup (big project) would clobber the saved set.
            FloatWindows = _floatsRestored ? CaptureFloatWindows() : baseState.FloatWindows,
            // full docked arrangement — same guard: before the floats are restored the tree
            // still holds the floated tools docked in their home regions, which must not
            // overwrite the real saved profile.
            Profile = _floatsRestored ? CaptureProfile() : baseState.Profile,
        };
    }

    // ---- floated-window persistence -----------------------------------
    // The dock TREE is rebuilt fresh each launch (a deserialized Dock 12 tree won't render), so
    // floated tool/document windows are persisted on their own and re-created at runtime by
    // re-floating the live dockables — the same operation a manual tear-off performs, which
    // renders correctly. Capture records each window's bounds + the ids of its dockables; restore
    // resolves those ids back to the live tool singletons / open documents and floats them.

    private bool _floatsRestored;
    private IReadOnlyList<ThIDE.Services.FloatWindowState>? _lastCapturedFloats;

    /// <summary>Snapshots the current floating windows (bounds + dockable ids). Returns a cached
    /// reference when nothing changed so the layout-state dedup keeps working.</summary>
    public IReadOnlyList<ThIDE.Services.FloatWindowState> CaptureFloatWindows()
    {
        var list = new List<ThIDE.Services.FloatWindowState>();
        if (_rootDock?.Windows is { } windows)
        {
            foreach (var w in windows)
            {
                if (w.Layout is not { } layout) continue;
                var ids = new List<string>();
                CollectLeafIds(layout, ids);
                if (ids.Count == 0) continue;

                double x = w.X, y = w.Y, width = w.Width, height = w.Height;
                bool maximized = false;
                if (w.Host is Avalonia.Controls.Window win)
                {
                    try
                    {
                        x = win.Position.X; y = win.Position.Y; width = win.Width; height = win.Height;
                        maximized = win.WindowState == Avalonia.Controls.WindowState.Maximized;
                    }
                    catch { /* window not realized — fall back to the model bounds */ }
                }
                list.Add(new ThIDE.Services.FloatWindowState
                {
                    X = x,
                    Y = y,
                    Width = width > 0 ? width : 700,
                    Height = height > 0 ? height : 500,
                    Maximized = maximized,
                    DockableIds = ids,
                });
            }
        }
        if (_lastCapturedFloats is not null && FloatsEqual(_lastCapturedFloats, list)) return _lastCapturedFloats;
        return _lastCapturedFloats = list;
    }

    private static bool FloatsEqual(
        IReadOnlyList<ThIDE.Services.FloatWindowState> a,
        IReadOnlyList<ThIDE.Services.FloatWindowState> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i]; var y = b[i];
            if (x.X != y.X || x.Y != y.Y || x.Width != y.Width || x.Height != y.Height) return false;
            if (x.Maximized != y.Maximized) return false;
            if (x.DockableIds.Count != y.DockableIds.Count) return false;
            for (int j = 0; j < x.DockableIds.Count; j++)
                if (!string.Equals(x.DockableIds[j], y.DockableIds[j], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static void CollectLeafIds(IDockable node, List<string> ids)
    {
        // ITool MUST precede IDocument — the Mvvm Tool base also implements IDocument (see the
        // CloneDockable note), so checking IDocument first would mis-bucket every tool.
        switch (node)
        {
            case ITool t when !string.IsNullOrEmpty(t.Id): ids.Add(t.Id!); break;
            case IDocument d when !string.IsNullOrEmpty(d.Id): ids.Add(d.Id!); break;
        }
        if (node is IDock dock && dock.VisibleDockables is { } list)
            foreach (var c in list) CollectLeafIds(c, ids);
    }

    /// <summary>Re-creates the persisted floating windows by re-floating the live dockables they
    /// held. Safe to call once startup (session restore) has materialized the documents; never
    /// throws (a window that can't be rebuilt is skipped).</summary>
    public void RestoreFloatWindows(IReadOnlyList<ThIDE.Services.FloatWindowState>? states)
    {
        _floatsRestored = true;   // from now on capture reflects the live float set
        if (_rootDock is null || states is null || states.Count == 0) return;
        var toolMap = ToolSingletonsById();
        foreach (var st in states)
        {
            try { RestoreOneFloatWindow(st, toolMap); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to restore a floated window."); }
        }
    }

    private void RestoreOneFloatWindow(
        ThIDE.Services.FloatWindowState st, Dictionary<string, IDockable> toolMap)
    {
        // Resolve ids → live dockables (tool singletons or already-open documents).
        var resolved = new List<IDockable>();
        foreach (var id in st.DockableIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            IDockable? d = toolMap.TryGetValue(id, out var tool) ? tool : FindRealDocumentById(_rootDock!, id);
            if (ReferenceEquals(d, _xvi) && !ShowXviPanel) continue;   // XVI hidden → don't resurrect a saved float
            // Skip a dockable that is already floating (e.g. the user tore it off again).
            if (d is not null && !resolved.Contains(d) && !IsInFloatWindow(d)) resolved.Add(d);
        }
        if (resolved.Count == 0) return;

        var first = resolved[0];
        FloatDockable(first);
        if (first.Owner is IDock target)
        {
            for (int i = 1; i < resolved.Count; i++)
            {
                var d = resolved[i];
                try { RemoveDockable(d, collapse: false); AddDockable(target, d); }
                catch { /* best-effort grouping */ }
            }
        }
        PositionFloatWindow(first, st);
    }

    private bool IsInFloatWindow(IDockable target)
    {
        if (_rootDock?.Windows is not { } windows) return false;
        foreach (var w in windows)
            if (w.Layout is { } wl && ContainsRef(wl, target)) return true;
        return false;
    }

    private void PositionFloatWindow(IDockable member, ThIDE.Services.FloatWindowState st)
    {
        if (_rootDock?.Windows is not { } windows) return;
        foreach (var w in windows)
        {
            if (w.Layout is not { } wl || !ContainsRef(wl, member)) continue;
            w.X = st.X; w.Y = st.Y; w.Width = st.Width; w.Height = st.Height;
            if (w.Host is Avalonia.Controls.Window win)
            {
                try
                {
                    win.Position = new Avalonia.PixelPoint((int)st.X, (int)st.Y);
                    win.Width = st.Width;
                    win.Height = st.Height;
                    // Position first, THEN maximize — so the window fills the monitor it was
                    // dropped on (the multi-monitor preset relies on this ordering).
                    if (st.Maximized)
                        win.WindowState = Avalonia.Controls.WindowState.Maximized;
                }
                catch { /* host not realized yet — model bounds above still apply */ }
            }
            return;
        }
    }

    private DocumentDock NewDocumentDock() => new()
    {
        Id = "Documents",
        Title = "Documents",
        IsCollapsable = false,
        CanCreateDocument = false,
        Proportion = 0.72,
        VisibleDockables = CreateList<IDockable>(),
    };

    /// <summary>The right-rail tools, gated by their feature settings.</summary>
    private IDockable[] RightToolset()
    {
        var list = new System.Collections.Generic.List<IDockable>();
        if (ShowXviPanel) list.Add(_xvi);                       // hidden by default (see ShowXviPanel)
        if (ThIDE.Services.EditorFeatureFlags.Compiled(ThIDE.Services.EditorFeature.Outline))
            list.Add(_outline);
        var s = _appSettings?.Current ?? ThIDE.Services.AppSettings.Default;
        if (s.EnableLivePreview) list.Add(_livePreview);
        if (s.EnableInAppViewer) list.Add(_mapViewer);
        if (s.EnableModel3DViewer) list.Add(_model3dViewer);   // (on by default)
        // opens in the central document well on demand (ShowToolInDocuments), not the rail.
        return list.ToArray();
    }

    private IRootDock BuildDefaultLayout()
    {
        // Re-apply the persisted pane sizes + active tabs onto the freshly-built (renderable)
        // default tree (#17).
        var st = _layoutState?.Current ?? ThIDE.Services.LayoutState.Default;

        var documentDock = NewDocumentDock();
        _documentDock = documentDock;

        // Object Browser opens in the central well by default (#10). It sits as the initial
        // tab there; opened files are added alongside it and become active.
        documentDock.VisibleDockables = CreateList<IDockable>(_objectBrowser);
        documentDock.ActiveDockable = _objectBrowser;

        var leftTools = new ToolDock
        {
            Id = "LeftTools",
            Title = "LeftTools",
            Alignment = Alignment.Left,
            Proportion = st.LeftProportion,
            // Workspace explorer + the "Project" pane (dashboard / surveys / audit).
            VisibleDockables = CreateList<IDockable>(_workspace, _project),
            ActiveDockable = _workspace,
        };

        var bottomTools = new ToolDock
        {
            Id = "BottomTools",
            Title = "BottomTools",
            Alignment = Alignment.Bottom,
            Proportion = st.BottomProportion,
            VisibleDockables = CreateList<IDockable>(_diagnostics, _compilerOutput, _generatedFiles, _log),
            ActiveDockable = BottomTabById(st.BottomActiveTab),
        };

        // Object Browser moved to the central well (#10); External Tools/Settings moved into
        // the Preferences window (#13). The right rail holds the document outline and the
        // preview panels (each gated by its setting), plus the XVI references panel when
        // ShowXviPanel is on. Its active tab is the first tool actually present.
        var rightToolset = RightToolset();
        var rightTools = new ToolDock
        {
            Id = "RightTools",
            Title = "RightTools",
            Alignment = Alignment.Right,
            Proportion = st.RightProportion,
            VisibleDockables = CreateList<IDockable>(rightToolset),
            ActiveDockable = rightToolset.Length > 0 ? rightToolset[0] : null,
        };

        var centerColumn = new ProportionalDock
        {
            Id = "CenterColumn",
            Orientation = Orientation.Vertical,
            Proportion = st.CenterProportion,
            VisibleDockables = CreateList<IDockable>(
                documentDock,
                new ProportionalDockSplitter(),
                bottomTools),
        };

        var mainRow = new ProportionalDock
        {
            Id = "MainRow",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftTools,
                new ProportionalDockSplitter(),
                centerColumn,
                new ProportionalDockSplitter(),
                rightTools),
        };

        var root = CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(mainRow);
        root.ActiveDockable = mainRow;
        root.DefaultDockable = mainRow;
        _rootDock = root;
        return root;
    }

    // ---- layout persistence (#10) ---------------------------------------

    private Dictionary<string, IDockable> ToolSingletonsById() => new(StringComparer.Ordinal)
    {
        ["Welcome"]        = _welcome,
        ["Workspace"]      = _workspace,
        ["ObjectBrowser"]  = _objectBrowser,
        ["Diagnostics"]    = _diagnostics,
        ["CompilerOutput"] = _compilerOutput,
        ["GeneratedFiles"] = _generatedFiles,
        ["Xvi"]            = _xvi,
        ["Outline"]        = _outline,
        ["Project"]        = _project,
        ["Log"]            = _log,
        ["LivePreview"]    = _livePreview,
        ["MapViewer"]      = _mapViewer,
        ["Model3DViewer"]  = _model3dViewer,
        ["StructuralGeology"] = _structuralGeology,
        ["StructuralPlot"] = _structuralPlot,
        ["BlenderAnimation"] = _blenderAnimation,
        ["Settings"]       = _settings,
    };

    /// <summary>Depth-first search (including float windows) for a dock of type <typeparamref name="T"/> with the given Id.</summary>
    // Replaces the deserializer's plain List<> collections with the Factory's observable
    // lists (and recurses through float windows) so the restored tree renders like a built one.
    private void NormalizeCollections(IDockable node)
    {
        if (node is IRootDock root && root.Windows is { Count: > 0 } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl) NormalizeCollections(wl);

        if (node is not IDock dock || dock.VisibleDockables is not { } list) return;
        foreach (var c in list) NormalizeCollections(c);
        var rebuilt = CreateList<IDockable>(list.ToArray());
        dock.VisibleDockables = rebuilt;
    }

    private static T? FindDockById<T>(IDockable node, string id) where T : class, IDock
    {
        if (node is T dock && string.Equals(node.Id, id, StringComparison.Ordinal)) return dock;
        if (node is IRootDock root && root.Windows is { } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl && FindDockById<T>(wl, id) is { } hit) return hit;
        if (node is IDock d && d.VisibleDockables is { } list)
            foreach (var c in list)
                if (FindDockById<T>(c, id) is { } hit) return hit;
        return null;
    }

    /// <summary>
    /// Loads a previously-saved dock layout, swapping the deserialized skeleton tools for
    /// the live DI singletons (by Id). Returns null on any error or version mismatch so the
    /// caller falls back to the fresh default layout (never throws).
    /// </summary>
    private IRootDock? TryLoadLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath)) return null;

            // A leftover sentinel means the previous attempt to apply this layout crashed
            // during render — discard it rather than crash again.
            if (File.Exists(LoadSentinelPath))
            {
                TryDelete(LayoutPath);
                TryDelete(LoadSentinelPath);
                return null;
            }

            var json = File.ReadAllText(LayoutPath);
            TryWrite(LoadSentinelPath, string.Empty);

            var root = _serializer.Deserialize<IRootDock>(json);
            if (root is null) { TryDelete(LoadSentinelPath); return null; }

            // The JSON deserializer fills VisibleDockables with plain non-observable List<>s,
            // which the Dock content controls don't render from. Rebuild every collection as
            // the Factory's observable list so the restored tree behaves like a freshly-built one.
            NormalizeCollections(root);

            // Swap the deserialized tool skeletons for the live DI singletons by Id. The main
            // document well is NOT swapped — we keep the deserialized one (it carries the open
            // tabs as placeholders) and adopt it as _documentDock so new opens still land here.
            var map = ToolSingletonsById();
            SwapSingletons(root, map);

            if (FindDockById<DocumentDock>(root, "Documents") is not { } documentDock)
            { TryDelete(LoadSentinelPath); return null; }
            _documentDock = documentDock;

            // Require every tool + the document well to be present (guards version skew).
            foreach (var singleton in map.Values)
                if (!ContainsRef(root, singleton)) { TryDelete(LoadSentinelPath); return null; }

            _rootDock = root;
            _lastSavedJson = json; // identical layout — no need to immediately rewrite it
            return root;
        }
        catch (Exception ex)
        {
            // Corrupt/incompatible layout — drop it so it can't fail again.
            _logger?.LogWarning(ex, "Saved dock layout could not be loaded; discarding {Path}.", LayoutPath);
            TryDelete(LayoutPath);
            TryDelete(LoadSentinelPath);
            return null;
        }
    }

    /// <summary>
    /// Confirms the loaded layout rendered successfully (clears the crash sentinel). Call
    /// once the main window is shown so the next launch trusts the saved layout (task 2).
    /// </summary>
    public void ConfirmLayoutLoaded() => TryDelete(LoadSentinelPath);

    /// <summary>
    /// Serializes the current layout to disk. Non-destructive (open documents are excluded by
    /// the serializer, not by mutating the live tree) and deduplicated, so it is safe to call
    /// continuously — which is how the layout survives a hard stop from the debugger (task 2).
    /// </summary>
    public void SaveLayout()
    {
        if (!PersistDockLayout) return; // dock-tree persistence disabled (see CreateLayout)
        try
        {
            if (_rootDock is null) return;
            // Serialize a skeleton of plain Dock base types (Id + structure, no documents,
            // no custom view-model state) so the layout always round-trips; the live tools
            // are substituted back by Id on load via SwapSingletons.
            var json = _serializer.Serialize<IRootDock>(BuildRootSkeleton(_rootDock));
            if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal)) return; // unchanged
            var dir = Path.GetDirectoryName(LayoutPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LayoutPath, json);
            _lastSavedJson = json;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist dock layout to {Path}.", LayoutPath);
        }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static void TryWrite(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
        }
        catch { }
    }

    // ---- layout skeleton (base-type clone for serialization) ------------
    // We never serialize our live view-models: tool VMs have cyclic Owner/Factory back-refs
    // and documents have no parameterless ctor. Instead we clone the layout into plain Dock
    // base types carrying only Id + structure. On load these deserialize cleanly (the stock
    // serializer's discriminators handle the interfaces) and SwapSingletons restores the
    // live singletons by Id. Open documents are dropped and reopened via session restore.

    private IRootDock BuildRootSkeleton(IRootDock src)
    {
        var clone = new RootDock
        {
            Id = src.Id,
            Title = src.Title,
            IsCollapsable = src.IsCollapsable,
            Proportion = src.Proportion,
            VisibleDockables = CloneChildren(src),
        };
        BindActiveAndDefault(clone, src);
        clone.Windows = CloneWindows(src);
        return clone;
    }

    // Floating windows (torn-off tools/documents): clone each window's bounds + state and
    // its layout skeleton so float position/size and which tabs are floated round-trip.
    // A window whose layout clones to nothing (e.g. only documents that were dropped in an
    // older skeleton) is skipped so we never restore an empty ghost window.
    private IList<IDockWindow> CloneWindows(IRootDock src)
    {
        var list = CreateList<IDockWindow>();
        if (src.Windows is not { } windows) return list;
        foreach (var w in windows)
        {
            if (w.Layout is not { } layout || BuildRootSkeleton(layout) is not { } clonedLayout) continue;
            if (clonedLayout.VisibleDockables is not { Count: > 0 }) continue;
            list.Add(new Dock.Model.Mvvm.Core.DockWindow
            {
                Id = w.Id,
                Title = w.Title,
                X = w.X,
                Y = w.Y,
                Width = w.Width,
                Height = w.Height,
                Topmost = w.Topmost,
                Layout = clonedLayout,
            });
        }
        return list;
    }

    // NOTE: in Dock 12 the Mvvm `Tool` base also implements IDocument, so the ITool case
    // MUST precede the IDocument case — otherwise every tool is mistaken for a document and
    // dropped, leaving empty tool docks whose missing singletons fail the load integrity
    // check, which silently reset the layout to the default on every launch.
    private IDockable? CloneDockable(IDockable node) => node switch
    {
        IRootDock root => BuildRootSkeleton(root),
        IDocumentDock dd => CloneDocumentDock(dd),
        IProportionalDock pd => CloneProportional(pd),
        IToolDock td => CloneToolDock(td),
        IProportionalDockSplitter => new ProportionalDockSplitter(),
        ITool tool => new Tool { Id = tool.Id, Title = tool.Title },
        IDocument doc => new Document { Id = doc.Id, Title = doc.Title }, // placeholder; rehydrated on restore
        _ => null,
    };

    private IProportionalDock CloneProportional(IProportionalDock src)
    {
        var clone = new ProportionalDock
        {
            Id = src.Id,
            Title = src.Title,
            Proportion = src.Proportion,
            Orientation = src.Orientation,
            VisibleDockables = CloneChildren(src),
        };
        BindActiveAndDefault(clone, src);
        return clone;
    }

    private IToolDock CloneToolDock(IToolDock src)
    {
        var clone = new ToolDock
        {
            Id = src.Id,
            Title = src.Title,
            Proportion = src.Proportion,
            Alignment = src.Alignment,
            VisibleDockables = CloneChildren(src),
        };
        BindActiveAndDefault(clone, src);
        return clone;
    }

    // The document well keeps its position/Id and the IDENTITY of its open tabs (as plain
    // Document placeholders carrying the file path in Id), so the tab set, order, active tab
    // and which dock/float-window each tab lives in all round-trip. The real
    // FileDocumentViewModels (with their text/parse state) are rehydrated on restore by
    // re-opening each file and swapping it into its placeholder slot (see OpenDocument).
    private IDocumentDock CloneDocumentDock(IDocumentDock src)
    {
        var clone = new DocumentDock
        {
            Id = src.Id,
            Title = src.Title,
            Proportion = src.Proportion,
            IsCollapsable = src.IsCollapsable,
            CanCreateDocument = false,
            VisibleDockables = CloneChildren(src),
        };
        BindActiveAndDefault(clone, src);
        return clone;
    }

    private IList<IDockable> CloneChildren(IDock src)
    {
        var list = CreateList<IDockable>();
        if (src.VisibleDockables is { } children)
            foreach (var child in children)
                if (CloneDockable(child) is { } clone)
                    list.Add(clone);
        return list;
    }

    private static void BindActiveAndDefault(IDock clone, IDock src)
    {
        clone.ActiveDockable = FindById(clone, src.ActiveDockable?.Id);
        clone.DefaultDockable = FindById(clone, src.DefaultDockable?.Id);
    }

    private static IDockable? FindById(IDock dock, string? id) =>
        id is null || dock.VisibleDockables is not { } list
            ? null
            : list.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

    /// <summary>Replaces deserialized dockables whose Id matches a DI singleton with that singleton.</summary>
    private void SwapSingletons(IDockable node, Dictionary<string, IDockable> map)
    {
        if (node is IRootDock rootDock && rootDock.Windows is { } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl) SwapSingletons(wl, map);

        if (node is not IDock dock || dock.VisibleDockables is not { } list) return;

        for (int i = 0; i < list.Count; i++)
        {
            var child = list[i];
            if (child.Id is { } id && map.TryGetValue(id, out var singleton))
                list[i] = singleton;
            else
                SwapSingletons(child, map);
        }
        if (dock.ActiveDockable?.Id is { } aid && map.TryGetValue(aid, out var a)) dock.ActiveDockable = a;
        if (dock.DefaultDockable?.Id is { } did && map.TryGetValue(did, out var d)) dock.DefaultDockable = d;
    }

    private static bool ContainsRef(IDockable node, IDockable target)
    {
        if (ReferenceEquals(node, target)) return true;
        if (node is IRootDock root && root.Windows is { } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl && ContainsRef(wl, target)) return true;
        if (node is IDock dock && dock.VisibleDockables is { } list)
            foreach (var c in list)
                if (ContainsRef(c, target)) return true;
        return false;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>();

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"]           = () => _rootDock,
            ["Documents"]      = () => _documentDock,
            ["Welcome"]        = () => _welcome,
            ["Workspace"]      = () => _workspace,
            ["ObjectBrowser"]  = () => _objectBrowser,
            ["Diagnostics"]    = () => _diagnostics,
            ["CompilerOutput"] = () => _compilerOutput,
            ["GeneratedFiles"] = () => _generatedFiles,
            ["Xvi"]            = () => _xvi,
            ["Outline"]        = () => _outline,
            ["Project"]        = () => _project,
            ["Log"]            = () => _log,
            ["LivePreview"]    = () => _livePreview,
            ["MapViewer"]      = () => _mapViewer,
            ["Model3DViewer"]  = () => _model3dViewer,
            ["StructuralGeology"] = () => _structuralGeology,
            ["StructuralPlot"] = () => _structuralPlot,
            ["BlenderAnimation"] = () => _blenderAnimation,
            ["Settings"]       = () => _settings,
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);
    }

    private System.Windows.Input.ICommand? _activateDockableCommand;

    /// <summary>
    /// Brings a tab to the front — bound from the tab strip's "switch to an open tab" flyout, whose
    /// bindings are reflection-based, so it needs a real ICommand rather than a method binding.
    /// </summary>
    public System.Windows.Input.ICommand ActivateDockableCommand =>
        _activateDockableCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand<IDockable>(ActivateDockable);

    /// <summary>Selects <paramref name="dockable"/> in its own dock and gives it focus.</summary>
    public void ActivateDockable(IDockable? dockable)
    {
        if (dockable is null || _rootDock is null) return;
        SetActiveDockable(dockable);
        SetFocusedDockable(_rootDock, dockable);
    }

    /// <summary>
    /// Adds (or re-activates) a document tab. If a restore placeholder is holding this file's
    /// slot it is swapped out in place — preserving which dock / float window / tab position
    /// (and active state) the tab had when the layout was saved; otherwise the tab is added to
    /// the central document well.
    /// </summary>
    public void OpenDocument(FileDocumentViewModel document, bool activate = true)
    {
        if (_rootDock is null || _documentDock is null) return;

        // Already open anywhere (main well, a split dock, or a float window) → just activate.
        if (FindRealDocumentById(_rootDock, document.Id) is { } existing)
        {
            if (!activate) return;
            SetActiveDockable(existing);
            SetFocusedDockable(_rootDock, existing);
            return;
        }

        // A restore placeholder is holding this file's slot → swap the live document in.
        // activate:false (batch session restore) still swaps and keeps the well's active tab
        // valid, but doesn't steal global focus — only the batch's last file is activated, so
        // N restored tabs don't each materialize an editor view in turn.
        if (FindPlaceholderSlot(_rootDock, document.Id) is { } slot && slot.Dock.VisibleDockables is { } slotList)
        {
            InitDockable(document, slot.Dock);
            slotList[slot.Index] = document;
            if (ReferenceEquals(slot.Dock.ActiveDockable, slot.Placeholder))
                slot.Dock.ActiveDockable = document;
            if (!activate) return;
            SetActiveDockable(document);
            SetFocusedDockable(_rootDock, document);
            return;
        }

        AddDockable(_documentDock, document);
        if (!activate)
        {
            // A well needs an active tab to render its strip; adopt the first batch tab
            // without focusing the ones that follow.
            _documentDock.ActiveDockable ??= document;
            return;
        }
        SetActiveDockable(document);
        SetFocusedDockable(_rootDock, document);
    }

    /// <summary>
    /// Removes any restore placeholders whose file was not re-opened (e.g. it was deleted on
    /// disk), so a stale layout never leaves a blank "ghost" tab behind. Call once session
    /// restore has finished opening the previous session's files.
    /// </summary>
    public void RemoveUnresolvedPlaceholders()
    {
        if (_rootDock is null) return;
        foreach (var placeholder in CollectPlaceholders(_rootDock).ToList())
            RemoveDockable(placeholder, collapse: true);
    }

    // A restore placeholder is a plain Dock document standing in for a not-yet-reopened file.
    // Tools (IDockContent) and live documents (also IDockContent) are excluded.
    private static bool IsPlaceholder(IDockable d) => d is IDocument && d is not IDockContent;

    private static FileDocumentViewModel? FindRealDocumentById(IDockable node, string id)
    {
        if (node is FileDocumentViewModel f && string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase))
            return f;
        if (node is IRootDock root && root.Windows is { } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl && FindRealDocumentById(wl, id) is { } hit) return hit;
        if (node is IDock d && d.VisibleDockables is { } list)
            foreach (var c in list)
                if (FindRealDocumentById(c, id) is { } hit) return hit;
        return null;
    }

    private readonly record struct PlaceholderSlot(IDock Dock, int Index, IDockable Placeholder);

    private static PlaceholderSlot? FindPlaceholderSlot(IDockable node, string id)
    {
        if (node is IRootDock root && root.Windows is { } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl && FindPlaceholderSlot(wl, id) is { } hit) return hit;
        if (node is IDock d && d.VisibleDockables is { } list)
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (IsPlaceholder(c) && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                    return new PlaceholderSlot(d, i, c);
                if (FindPlaceholderSlot(c, id) is { } hit) return hit;
            }
        return null;
    }

    private static IEnumerable<IDockable> CollectPlaceholders(IDockable node)
    {
        if (node is IRootDock root && root.Windows is { } windows)
            foreach (var w in windows)
                if (w.Layout is { } wl)
                    foreach (var p in CollectPlaceholders(wl)) yield return p;
        if (node is IDock d && d.VisibleDockables is { } list)
            foreach (var c in list)
                if (IsPlaceholder(c)) yield return c;
                else
                    foreach (var p in CollectPlaceholders(c)) yield return p;
    }
}
