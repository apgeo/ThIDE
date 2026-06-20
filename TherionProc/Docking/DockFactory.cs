// Builds and owns the VS-classic dock layout:
//   Left:   Workspace
//   Center: document well (open .th files)  +  Bottom: Diagnostics / Compiler Output / Generated Files
//   Right:  Object Browser / XVI / Settings
// Also wires the locators Dock needs to materialize tools and floating windows.

using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Docking;

public sealed class DockFactory : Factory
{
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

    public override IRootDock CreateLayout()
    {
        var documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            Proportion = 0.72,
            VisibleDockables = CreateList<IDockable>(),
        };
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
