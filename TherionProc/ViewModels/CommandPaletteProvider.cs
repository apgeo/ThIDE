// #4 — Command palette (Ctrl+Shift+P). Builds a flat searchable list of application commands and
// reuses the generic QuickPick overlay. Categories: Editor actions (act on the focused editor),
// File / Build / View / Navigation (MainWindowViewModel commands), Windows + Settings sections
// (raised as view events), and Documentation terms (open the thbook). A command may push a
// follow-up quick-pick step for a parameter (e.g. "Open File…" pushes the Go-to-File picker).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Therion.Core;
using Therion.Semantics;
using TherionProc.Editor;
using TherionProc.Services;
using TherionProc.ViewModels.QuickPick;

namespace TherionProc.ViewModels;

public sealed class CommandPaletteProvider
{
    private readonly MainWindowViewModel _vm;
    private readonly IThbookDocumentationService? _docs;
    private readonly IDocumentService? _documents;
    private readonly IWorkspaceSession? _session;
    private readonly Action<QuickPickViewModel> _push;
    private readonly int _stationLimit;

    private List<QuickPickItem>? _wsSymbols;   // cached per palette session
    private List<QuickPickItem>? _docSymbols;

    public CommandPaletteProvider(MainWindowViewModel vm, IThbookDocumentationService? docs,
        IDocumentService? documents, IWorkspaceSession? session, Action<QuickPickViewModel> push,
        int stationLimit = 4000)
    {
        _vm = vm;
        _docs = docs;
        _documents = documents;
        _session = session;
        _push = push;
        _stationLimit = stationLimit > 0 ? stationLimit : int.MaxValue;
    }

    public QuickPickViewModel CreatePalette(string initialText = "")
    {
        var commands = BuildCommands();
        return new QuickPickViewModel(
            "Command Palette",
            "Type a command…   (@ document symbol · # workspace symbol · :42 go to line)",
            text => Route(commands, text),
            initialText);
    }

    // Prefix routing (VS-Code style): @ → document symbols, # → workspace symbols, :N → go to line.
    private IReadOnlyList<QuickPickItem> Route(List<QuickPickItem> commands, string text)
    {
        var t = (text ?? string.Empty).TrimStart();
        if (t.StartsWith('#')) return Filter(_wsSymbols ??= BuildSymbolItems(true), t[1..].Trim());
        if (t.StartsWith('@')) return Filter(_docSymbols ??= BuildSymbolItems(false), t[1..].Trim());
        if (t.StartsWith(':') && int.TryParse(t[1..].Trim(), out var line) && line > 0)
            return new[] { GoToLineItem(line) };
        return Filter(commands, text);
    }

    private static QuickPickItem GoToLineItem(int line) => new()
    {
        Title = $"Go to line {line}",
        Detail = "navigate",
        IconKey = "Icon.Search",
        Run = () => { if (TherionTextEditor.LastFocused is { } ed) ed.GoToLine(line); return Task.CompletedTask; },
    };

    private List<QuickPickItem> BuildCommands()
    {
        var list = new List<QuickPickItem>();

        // ---- File ----
        list.Add(VmCmd("File", "Open File…", _vm.OpenFileCommand, "Icon.File"));
        list.Add(VmCmd("File", "Open thconfig…", _vm.OpenThconfigCommand, "Icon.Config"));
        list.Add(VmCmd("File", "Open Folder…", _vm.OpenFolderCommand, "Icon.FolderOpen"));
        list.Add(VmCmd("File", "Save", _vm.SaveCommand, "Icon.File"));
        list.Add(Action("File", "Go to File…  (Ctrl+P)", () => { _vm.ShowQuickOpen(); return Task.CompletedTask; }, "Icon.Search"));

        // ---- Build ----
        list.Add(VmCmd("Build", "Build current thconfig", _vm.Build.BuildCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Rebuild", _vm.Build.RebuildCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Cancel build", _vm.Build.CancelCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Open in Loch", _vm.Build.OpenInLochCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Open in Aven", _vm.Build.OpenInAvenCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Open output folder", _vm.Build.OpenLastOutputFolderCommand, "Icon.Folder"));
        // BUILD-02/06: quick export + external round-trips.
        list.Add(VmCmd("Build", "Quick Export…", _vm.Build.ShowQuickExportCommand, "Icon.Map"));
        list.Add(VmCmd("Build", "Survex: dump3d (.3d → text)", _vm.Build.Dump3dCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Survex: extend (extended elevation)", _vm.Build.ExtendCommand, "Icon.Cube"));
        list.Add(VmCmd("Build", "Therion: print version", _vm.Build.PrintTherionVersionCommand, "Icon.Info"));
        list.Add(VmCmd("Build", "Therion: print environment", _vm.Build.PrintTherionEnvironmentCommand, "Icon.Info"));
        // BUILD-01: each export target parsed from the active thconfig.
        foreach (var target in _vm.Build.ExportTargets)
            list.Add(Action("Build", $"Build target: {target.Title}",
                () => { target.BuildCommand.Execute(null); return Task.CompletedTask; }, "Icon.Cube"));
        if (_session is { Candidates.Count: > 0 })
            list.Add(Action("Build", "Build a specific thconfig…", () => { PushThconfigBuild(); return Task.CompletedTask; }, "Icon.Config"));

        // ---- View / panels ----
        list.Add(VmCmd("View", "Toggle Object Browser", _vm.ToggleObjectBrowserCommand, "Icon.Map"));
        list.Add(VmCmd("View", "Toggle Workspace", _vm.ToggleWorkspaceExplorerCommand, "Icon.Folder"));
        list.Add(VmCmd("View", "Toggle Diagnostics", _vm.ToggleDiagnosticsCommand, "Icon.Search"));
        list.Add(VmCmd("View", "Toggle Outline", _vm.ToggleOutlineCommand, "Icon.Map"));
        list.Add(VmCmd("View", "Toggle Project (Dashboard / Surveys / Audit)", _vm.ToggleProjectCommand, "Icon.NodeGraph"));
        list.Add(VmCmd("View", "Toggle Log", _vm.ToggleLogCommand, "Icon.Search"));
        if (_vm.LivePreviewEnabled)
            list.Add(VmCmd("View", "Toggle Live Preview", _vm.ToggleLivePreviewCommand, "Icon.Map"));
        if (_vm.MapViewerEnabled)
            list.Add(VmCmd("View", "Toggle Map Viewer", _vm.ToggleMapViewerCommand, "Icon.Map"));
        if (_vm.Model3DViewerEnabled)
            list.Add(VmCmd("View", "Toggle 3D Viewer", _vm.ToggleModel3DViewerCommand, "Icon.Cube"));
        list.Add(VmCmd("View", "Split Editor (Float)", _vm.SplitEditorCommand, "Icon.File"));
        list.Add(VmCmd("View", "Reset Layout", _vm.ResetLayoutCommand, "Icon.Folder"));
        list.Add(VmCmd("View", "Float Active Document", _vm.FloatActiveDocumentCommand, "Icon.File"));

        // ---- Navigation / search ----
        list.Add(VmCmd("Go", "Back", _vm.GoBackCommand, "Icon.Search"));
        list.Add(VmCmd("Go", "Forward", _vm.GoForwardCommand, "Icon.Search"));
        list.Add(VmCmd("Search", "Find in Files", _vm.ShowFindInFilesCommand, "Icon.Search"));
        list.Add(VmCmd("Search", "Replace in Files", _vm.ShowReplaceInFilesCommand, "Icon.Search"));
        list.Add(VmCmd("Edit", "Rename Symbol", _vm.RenameSymbolCommand, "Icon.File"));

        // ---- Symbols / identifiers (pushed sub-steps; search like the doc terms) ----
        list.Add(Action("Go", "Go to Symbol in Workspace…", () => { PushSymbolSearch(workspace: true); return Task.CompletedTask; }, "Icon.Map"));
        list.Add(Action("Go", "Go to Symbol in Document…", () => { PushSymbolSearch(workspace: false); return Task.CompletedTask; }, "Icon.Map"));
        list.Add(Editor("Go to Line…", ed => ed.MenuGoToLine()));

        // ---- Editor view toggles ----
        list.Add(Action("View", "Toggle Word Wrap", () => { _vm.WordWrap = !_vm.WordWrap; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action("View", "Toggle Whitespace", () => { _vm.ShowWhitespace = !_vm.ShowWhitespace; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action("View", "Toggle Minimap", () => { _vm.ShowMinimap = !_vm.ShowMinimap; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action("View", "Toggle Indentation Guides", () => { _vm.ShowIndentGuides = !_vm.ShowIndentGuides; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action("View", "Toggle End-of-Line Markers", () => { _vm.ShowEndOfLine = !_vm.ShowEndOfLine; return Task.CompletedTask; }, "Icon.File"));

        // ---- File extras ----
        if (_documents is not null)
            list.Add(Action("File", "Reveal Active File in Workspace", () =>
            {
                if (_documents.CurrentPath is { Length: > 0 } p) _documents.RequestSelectFileInWorkspace(p);
                return Task.CompletedTask;
            }, "Icon.Folder"));

        // ---- Language ----
        list.Add(VmCmd("Language", "Switch to English", _vm.SwitchToEnglishCommand, "Icon.Config"));
        list.Add(VmCmd("Language", "Switch to Romanian", _vm.SwitchToRomanianCommand, "Icon.Config"));

        // ---- Editor actions (act on the focused editor) ----
        list.Add(Editor("Cut", ed => ed.MenuCut()));
        list.Add(Editor("Copy", ed => ed.MenuCopy()));
        list.Add(Editor("Paste", ed => ed.MenuPaste()));
        list.Add(Editor("Delete", ed => ed.MenuDelete()));
        list.Add(Editor("Select All", ed => ed.MenuSelectAll()));
        list.Add(Editor("UPPERCASE selection", ed => ed.MenuUpperCase()));
        list.Add(Editor("lowercase selection", ed => ed.MenuLowerCase()));
        list.Add(Editor("Toggle Comment", ed => ed.MenuToggleComment()));
        list.Add(Editor("Fold All", ed => ed.MenuFoldAll()));
        list.Add(Editor("Unfold All", ed => ed.MenuUnfoldAll()));
        // QOL-07: line operations.
        list.Add(Editor("Duplicate Line(s)", ed => ed.DuplicateLines()));
        list.Add(Editor("Move Line(s) Up", ed => ed.MoveLinesUp()));
        list.Add(Editor("Move Line(s) Down", ed => ed.MoveLinesDown()));
        list.Add(Editor("Sort Selected Lines", ed => ed.SortSelectedLines()));
        // QOL-08: insert helpers.
        list.Add(Editor("Insert Today's Date", ed => ed.InsertDate()));
        list.Add(Editor("Insert Team Member", ed => ed.InsertTeamMember()));
        list.Add(Editor("Add Bookmark…", ed => ed.MenuAddBookmark()));
        list.Add(Editor("Find", ed => ed.MenuFind()));
        list.Add(Editor("Replace", ed => ed.MenuReplace()));
        list.Add(Editor("Format Document", ed => ed.FormatDocument()));
        list.Add(Editor("Quick Fix…  (Ctrl+.)", ed => ed.ShowQuickFixes()));
        list.Add(Editor("Go to Matching Block", ed => ed.GoToMatchingBlock()));
        list.Add(Editor("Peek Definition", ed => ed.PeekDefinition()));
        list.Add(Editor("Step Into Included File", ed => ed.FollowIncludeUnderCaret()));
        list.Add(Editor("Rename Symbol (editor)", ed => ed.StartRename()));

        // ---- Windows ----
        list.Add(Action("Window", "Preferences…", () => { _vm.RaiseShowPreferences(null); return Task.CompletedTask; }, "Icon.Config"));
        list.Add(Action("Window", "About", () => { _vm.RaiseShowAbout(); return Task.CompletedTask; }, "Icon.Config"));
        list.Add(Action("Window", "Therion Book (documentation)", () => { _vm.RaiseShowThbook(); return Task.CompletedTask; }, "Icon.Map"));
        list.Add(Action("Window", "Bookmarks", () => { _vm.RaiseShowBookmarks(); return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action("Window", "Relational Map", () => { _vm.RaiseShowRelationalMap(); return Task.CompletedTask; }, "Icon.Map"));

        // ---- Settings sections (open Preferences at that tab) ----
        foreach (var (id, title) in SettingsSections)
            list.Add(Action("Settings", $"Settings: {title}", () => { _vm.RaiseShowPreferences(id); return Task.CompletedTask; }, "Icon.Config"));

        // ---- Documentation: a parameterized command that pushes a focused term-search sub-step ----
        if (_docs is { IsAvailable: true })
            list.Add(Action("Docs", "Search Documentation…", () => { PushDocSearch(); return Task.CompletedTask; }, "Icon.Map"));

        return list;
    }

    // Pushes a second quick-pick step listing every documented term (the "parameter" of the
    // Search Documentation command); accepting one opens the thbook at its page.
    private void PushDocSearch()
    {
        if (_docs is null) return;
        var terms = _docs.Terms
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(t => new QuickPickItem
            {
                Title = t,
                Detail = "documentation",
                IconKey = "Icon.Map",
                NameLower = t.ToLowerInvariant(),
                PathLower = "docs",
                Run = () => { _docs!.Open(t); return Task.CompletedTask; },
            })
            .ToList();
        _push(new QuickPickViewModel("Documentation", "Search a term…", text => Filter(terms, text)));
    }

    // Pushes a second step listing project identifiers (surveys / stations / scraps / maps);
    // accepting one navigates to its declaration. Workspace mode aggregates across files.
    private void PushSymbolSearch(bool workspace)
    {
        var items = workspace ? (_wsSymbols ??= BuildSymbolItems(true)) : (_docSymbols ??= BuildSymbolItems(false));
        if (items.Count == 0)
            items = new List<QuickPickItem> { new() { Title = "(no symbols — open and parse a project first)" } };
        var title = workspace ? "Go to Symbol (workspace)" : "Go to Symbol (document)";
        _push(new QuickPickViewModel(title, "Search surveys, stations, scraps, maps…", text => Filter(items, text)));
    }

    // Parameterized command: pick a thconfig from the workspace, activate it, and build.
    private void PushThconfigBuild()
    {
        if (_session is null) return;
        var items = _session.Candidates.Select(c =>
        {
            var name = System.IO.Path.GetFileName(c.FullPath);
            return new QuickPickItem
            {
                Title = name,
                Detail = c.FullPath,
                IconKey = "Icon.Config",
                NameLower = name.ToLowerInvariant(),
                PathLower = c.FullPath.ToLowerInvariant(),
                Run = async () =>
                {
                    await _session.SetActiveThconfigAsync(c.FullPath).ConfigureAwait(true);
                    if (_vm.Build.BuildCommand.CanExecute(null)) _vm.Build.BuildCommand.Execute(null);
                },
            };
        }).ToList();
        _push(new QuickPickViewModel("Build thconfig", "Pick a thconfig to build…", text => Filter(items, text)));
    }

    private List<QuickPickItem> BuildSymbolItems(bool workspace)
    {
        var list = new List<QuickPickItem>();
        if (_documents is null) return list;

        if (workspace && _documents.Workspace is { } ws)
        {
            foreach (var kv in ws.SurveysByFullName) AddSymbol(list, kv.Key, kv.Value.DeclarationSpan, "survey");
            foreach (var kv in ws.ScrapsById)        AddSymbol(list, kv.Key, kv.Value.DeclarationSpan, "scrap");
            foreach (var kv in ws.MapsById)          AddSymbol(list, kv.Key, kv.Value.DeclarationSpan, "map");
            int n = 0;
            foreach (var kv in ws.StationsByQn) { AddSymbol(list, kv.Key, kv.Value.DeclarationSpan, "station"); if (++n >= _stationLimit) break; }
        }
        // PERF-03: the live graph isn't built yet (or is being rebuilt) → fall back to the
        // persisted symbol index so workspace symbol search still works instantly on reopen.
        else if (workspace && _session?.SymbolIndex is { Symbols.Count: > 0 } idx)
        {
            int n = 0;
            foreach (var s in idx.Symbols)
            {
                if (s.Kind == "station" && ++n > _stationLimit) continue;
                AddSymbol(list, s.Name, s.ToSpan(), s.Kind);
            }
        }
        else if (_documents.CurrentSemantics is { } model)
        {
            foreach (var kv in model.Surveys) AddSymbol(list, kv.Key.ToString(), kv.Value.DeclarationSpan, "survey");
            foreach (var kv in model.Scraps)  AddSymbol(list, kv.Key, kv.Value.DeclarationSpan, "scrap");
            foreach (var kv in model.Maps)    AddSymbol(list, kv.Key, kv.Value.DeclarationSpan, "map");
            int n = 0;
            foreach (var kv in model.Stations) { AddSymbol(list, kv.Key.ToString(), kv.Value.DeclarationSpan, "station"); if (++n >= _stationLimit) break; }
        }
        return list;
    }

    private void AddSymbol(List<QuickPickItem> list, string name, SourceSpan span, string kind)
    {
        if (string.IsNullOrEmpty(name) || span.IsEmpty) return;
        var file = string.IsNullOrEmpty(span.FilePath) ? string.Empty : "  " + System.IO.Path.GetFileName(span.FilePath);
        list.Add(new QuickPickItem
        {
            Title = name,
            Detail = $"{kind}{file}  :{span.Start.Line}",
            IconKey = kind switch { "survey" => "Icon.Cube", "station" => "Icon.Search", _ => "Icon.Map" },
            NameLower = name.ToLowerInvariant(),
            PathLower = kind,
            Run = () => { _ = _documents!.NavigateToSpanAsync(span); return Task.CompletedTask; },
        });
    }

    private static readonly (string Id, string Title)[] SettingsSections =
    {
        ("general", "General"), ("theme", "Theme & Colors"), ("editor", "Editor"),
        ("editorfeatures", "Editor Features"), ("performance", "Performance"),
        ("workspace", "Workspace"), ("build", "Build & Output"), ("external", "External Tools"),
        ("keyboard", "Keyboard Shortcuts"),
    };

    // ---- item factories ----

    private static QuickPickItem VmCmd(string category, string title, ICommand command, string iconKey) => new()
    {
        Title = title,
        Detail = category,
        IconKey = iconKey,
        NameLower = title.ToLowerInvariant(),
        PathLower = category.ToLowerInvariant(),
        Run = () => { if (command.CanExecute(null)) command.Execute(null); return Task.CompletedTask; },
    };

    private static QuickPickItem Action(string category, string title, Func<Task> run, string iconKey) => new()
    {
        Title = title,
        Detail = category,
        IconKey = iconKey,
        NameLower = title.ToLowerInvariant(),
        PathLower = category.ToLowerInvariant(),
        Run = run,
    };

    private static QuickPickItem Editor(string title, Action<TherionTextEditor> act) => new()
    {
        Title = title,
        Detail = "Editor",
        IconKey = "Icon.File",
        NameLower = title.ToLowerInvariant(),
        PathLower = "editor",
        Run = () =>
        {
            if (TherionTextEditor.LastFocused is { } ed) act(ed);
            return Task.CompletedTask;
        },
    };

    // ---- filtering ----

    private static IReadOnlyList<QuickPickItem> Filter(List<QuickPickItem> all, string text)
    {
        var q = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Length == 0) return all;
        var scored = new List<(QuickPickItem Item, int Score, int Index)>();
        for (int i = 0; i < all.Count; i++)
            if (QuickPickMatcher.Score(q, all[i].NameLower, all[i].PathLower) is { } sc)
                scored.Add((all[i], sc, i));
        return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Index).Select(s => s.Item).Take(400).ToList();
    }
}
