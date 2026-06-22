// Builds and owns the VS-classic dock layout:
//   Left:   Workspace
//   Center: document well (open .th files)  +  Bottom: Diagnostics / Compiler Output / Generated Files
//   Right:  Object Browser / XVI / Settings
// Also wires the locators Dock needs to materialize tools and floating windows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization.Metadata;
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

    private readonly IDockSerializer _serializer = CreateSerializer();
    private string? _lastSavedJson;

    // Custom Tool subclasses fall back to reflection serialization, which would follow
    // the Owner/Factory/Context back-references and hit a cycle. Strip those nav props
    // from every IDockable so only the structural layout (Id/Title/Proportion/children)
    // round-trips.
    private static IDockSerializer CreateSerializer()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static ti =>
        {
            if (!typeof(IDockable).IsAssignableFrom(ti.Type)) return;
            bool isDocumentDock = typeof(DocumentDock).IsAssignableFrom(ti.Type);
            for (int i = ti.Properties.Count - 1; i >= 0; i--)
            {
                var name = ti.Properties[i].Name;
                if (name is "Owner" or "Factory" or "Context" or "OriginalOwner" or "FocusedDockable")
                    ti.Properties.RemoveAt(i);
                // Never persist open documents (FileDocumentViewModel has no parameterless
                // ctor, so it can't round-trip). Dropping the document well's children keeps
                // the layout deserializable; session restore reopens the files (task 2).
                else if (isDocumentDock && name is "VisibleDockables" or "ActiveDockable" or "DefaultDockable")
                    ti.Properties.RemoveAt(i);
            }
        });
        return new DockSerializer(resolver);
    }
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
        SettingsToolViewModel settings)
    {
        _workspace = workspace;
        _objectBrowser = objectBrowser;
        _diagnostics = diagnostics;
        _compilerOutput = compilerOutput;
        _generatedFiles = generatedFiles;
        _xvi = xvi;
        _settings = settings;
    }

    /// <summary>The central document well; <see cref="OpenDocument"/> adds tabs here.</summary>
    public DocumentDock? DocumentDock => _documentDock;

    public override IRootDock CreateLayout() => TryLoadLayout() ?? BuildDefaultLayout();

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
            VisibleDockables = CreateList<IDockable>(_objectBrowser, _xvi, _settings),
            ActiveDockable = _objectBrowser,
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

    private Dictionary<string, IDockable> SingletonsById() => new(StringComparer.Ordinal)
    {
        ["Documents"]      = _documentDock!,
        ["Workspace"]      = _workspace,
        ["ObjectBrowser"]  = _objectBrowser,
        ["Diagnostics"]    = _diagnostics,
        ["CompilerOutput"] = _compilerOutput,
        ["GeneratedFiles"] = _generatedFiles,
        ["Xvi"]            = _xvi,
        ["Settings"]       = _settings,
    };

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

            _documentDock = NewDocumentDock();   // fresh empty well to swap in
            var map = SingletonsById();
            SwapSingletons(root, map);

            // Require every tool + the document well to be present (guards version skew).
            foreach (var singleton in map.Values)
                if (!ContainsRef(root, singleton)) { TryDelete(LoadSentinelPath); return null; }

            _rootDock = root;
            _lastSavedJson = json; // identical layout — no need to immediately rewrite it
            return root;
        }
        catch
        {
            // Corrupt/incompatible layout — drop it so it can't fail again.
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
        try
        {
            if (_rootDock is null) return;
            var json = _serializer.Serialize(_rootDock);
            if (string.Equals(json, _lastSavedJson, StringComparison.Ordinal)) return; // unchanged
            var dir = Path.GetDirectoryName(LayoutPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(LayoutPath, json);
            _lastSavedJson = json;
        }
        catch { /* best-effort */ }
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

    /// <summary>Adds (or re-activates) a document tab in the central document well.</summary>
    public void OpenDocument(FileDocumentViewModel document)
    {
        if (_documentDock is null) return;

        // De-dupe by file path (Id): activate the existing tab instead of re-adding.
        if (_documentDock.VisibleDockables is { } existing)
        {
            foreach (var d in existing)
            {
                if (d is FileDocumentViewModel f &&
                    string.Equals(f.Id, document.Id, StringComparison.OrdinalIgnoreCase))
                {
                    SetActiveDockable(f);
                    SetFocusedDockable(_rootDock!, f);
                    return;
                }
            }
        }

        AddDockable(_documentDock, document);
        SetActiveDockable(document);
        SetFocusedDockable(_rootDock!, document);
    }
}
