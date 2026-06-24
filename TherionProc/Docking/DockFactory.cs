// Builds and owns the VS-classic dock layout:
//   Left:   Workspace
//   Center: document well (open .th files)  +  Bottom: Diagnostics / Compiler Output / Generated Files
//   Right:  Object Browser / XVI / Settings
// Also wires the locators Dock needs to materialize tools and floating windows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Serializer.SystemTextJson;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Docking;

public sealed class DockFactory : Factory
{
    private static readonly string LayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TherionProc", "dock-layout.json");

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
    private readonly WorkspaceExplorerToolViewModel _workspace;
    private readonly ObjectBrowserToolViewModel _objectBrowser;
    private readonly DiagnosticsToolViewModel _diagnostics;
    private readonly CompilerOutputToolViewModel _compilerOutput;
    private readonly GeneratedFilesToolViewModel _generatedFiles;
    private readonly XviToolViewModel _xvi;
    private readonly SettingsToolViewModel _settings;

    private IRootDock? _rootDock;
    private DocumentDock? _documentDock;

    public DockFactory(
        WorkspaceExplorerToolViewModel workspace,
        ObjectBrowserToolViewModel objectBrowser,
        DiagnosticsToolViewModel diagnostics,
        CompilerOutputToolViewModel compilerOutput,
        GeneratedFilesToolViewModel generatedFiles,
        XviToolViewModel xvi,
        SettingsToolViewModel settings,
        ILogger<DockFactory>? logger = null)
    {
        _workspace = workspace;
        _objectBrowser = objectBrowser;
        _diagnostics = diagnostics;
        _compilerOutput = compilerOutput;
        _generatedFiles = generatedFiles;
        _xvi = xvi;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>The central document well; <see cref="OpenDocument"/> adds tabs here.</summary>
    public DocumentDock? DocumentDock => _documentDock;

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

    // Dock-tree persistence is DISABLED: Dock.Avalonia 12 does not render content for a
    // deserialized-and-reswapped layout tree — the tab strips appear but the content controls
    // never instantiate their views, leaving every panel blank. Building the default layout
    // (which renders correctly) avoids that. Window geometry still persists via ILayoutService
    // and the open files reopen via session restore. Re-enable only once a restore strategy
    // that produces a renderable tree is in place (e.g. rebuilding rather than deserializing).
    private const bool PersistDockLayout = false;

    public override IRootDock CreateLayout() =>
        (PersistDockLayout ? TryLoadLayout() : null) ?? BuildDefaultLayout();

    private DocumentDock NewDocumentDock() => new()
    {
        Id = "Documents",
        Title = "Documents",
        IsCollapsable = false,
        CanCreateDocument = false,
        Proportion = 0.72,
        VisibleDockables = CreateList<IDockable>(),
    };

    private IRootDock BuildDefaultLayout()
    {
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
            Proportion = 0.18,
            VisibleDockables = CreateList<IDockable>(_workspace),
            ActiveDockable = _workspace,
        };

        var bottomTools = new ToolDock
        {
            Id = "BottomTools",
            Title = "BottomTools",
            Alignment = Alignment.Bottom,
            Proportion = 0.28,
            VisibleDockables = CreateList<IDockable>(_diagnostics, _compilerOutput, _generatedFiles),
            ActiveDockable = _diagnostics,
        };

        var rightTools = new ToolDock
        {
            Id = "RightTools",
            Title = "RightTools",
            Alignment = Alignment.Right,
            Proportion = 0.22,
            // Object Browser moved to the central well (#10); External Tools/Settings moved into
            // the Preferences window (#13), so the right rail keeps just XVI references.
            VisibleDockables = CreateList<IDockable>(_xvi),
            ActiveDockable = _xvi,
        };

        var centerColumn = new ProportionalDock
        {
            Id = "CenterColumn",
            Orientation = Orientation.Vertical,
            Proportion = 0.60,
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
        ["Workspace"]      = _workspace,
        ["ObjectBrowser"]  = _objectBrowser,
        ["Diagnostics"]    = _diagnostics,
        ["CompilerOutput"] = _compilerOutput,
        ["GeneratedFiles"] = _generatedFiles,
        ["Xvi"]            = _xvi,
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
            ["Workspace"]      = () => _workspace,
            ["ObjectBrowser"]  = () => _objectBrowser,
            ["Diagnostics"]    = () => _diagnostics,
            ["CompilerOutput"] = () => _compilerOutput,
            ["GeneratedFiles"] = () => _generatedFiles,
            ["Xvi"]            = () => _xvi,
            ["Settings"]       = () => _settings,
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow(),
        };

        base.InitLayout(layout);
    }

    /// <summary>
    /// Adds (or re-activates) a document tab. If a restore placeholder is holding this file's
    /// slot it is swapped out in place — preserving which dock / float window / tab position
    /// (and active state) the tab had when the layout was saved; otherwise the tab is added to
    /// the central document well.
    /// </summary>
    public void OpenDocument(FileDocumentViewModel document)
    {
        if (_rootDock is null || _documentDock is null) return;

        // Already open anywhere (main well, a split dock, or a float window) → just activate.
        if (FindRealDocumentById(_rootDock, document.Id) is { } existing)
        {
            SetActiveDockable(existing);
            SetFocusedDockable(_rootDock, existing);
            return;
        }

        // A restore placeholder is holding this file's slot → swap the live document in.
        if (FindPlaceholderSlot(_rootDock, document.Id) is { } slot && slot.Dock.VisibleDockables is { } slotList)
        {
            InitDockable(document, slot.Dock);
            slotList[slot.Index] = document;
            if (ReferenceEquals(slot.Dock.ActiveDockable, slot.Placeholder))
                slot.Dock.ActiveDockable = document;
            SetActiveDockable(document);
            SetFocusedDockable(_rootDock, document);
            return;
        }

        AddDockable(_documentDock, document);
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
