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
using ThIDE.Editor;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels.QuickPick;

namespace ThIDE.ViewModels;

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

    // Localized string lookup (Strings.resx / .ro.resx).
    private static string L(string key) => Tr.Get(key);

    public QuickPickViewModel CreatePalette(string initialText = "")
    {
        var commands = BuildCommands();
        return new QuickPickViewModel(
            L("Cmd_Title"),
            L("Cmd_Watermark"),
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
        Title = string.Format(L("Cmd_GoToLineFmt"), line),
        Detail = L("Cmd_Cat_Navigate"),
        IconKey = "Icon.Search",
        Run = () => { if (TherionTextEditor.LastFocused is { } ed) ed.GoToLine(line); return Task.CompletedTask; },
    };

    private List<QuickPickItem> BuildCommands()
    {
        var list = new List<QuickPickItem>();

        // ---- File ----
        list.Add(VmCmd(L("Cmd_Cat_File"), L("Cmd_OpenFile"), _vm.OpenFileCommand, "Icon.File"));
        list.Add(VmCmd(L("Cmd_Cat_File"), L("Cmd_OpenThconfig"), _vm.OpenThconfigCommand, "Icon.Config"));
        list.Add(VmCmd(L("Cmd_Cat_File"), L("Cmd_OpenFolder"), _vm.OpenFolderCommand, "Icon.FolderOpen"));
        list.Add(VmCmd(L("Cmd_Cat_File"), L("Cmd_Save"), _vm.SaveCommand, "Icon.File"));
        list.Add(Action(L("Cmd_Cat_File"), L("Cmd_GoToFile"), () => { _vm.ShowQuickOpen(); return Task.CompletedTask; }, "Icon.Search"));

        // ---- Build ----
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_BuildCurrent"), _vm.Build.BuildCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_Rebuild"), _vm.Build.RebuildCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_CancelBuild"), _vm.Build.CancelCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_OpenLoch"), _vm.Build.OpenInLochCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_OpenAven"), _vm.Build.OpenInAvenCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Proj_OpenOutputFolder"), _vm.Build.OpenLastOutputFolderCommand, "Icon.Folder"));
        // quick export + external round-trips.
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Menu_Build_QuickExport"), _vm.Build.ShowQuickExportCommand, "Icon.Map"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_Dump3d"), _vm.Build.Dump3dCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_Extend"), _vm.Build.ExtendCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_PrintVersion"), _vm.Build.PrintTherionVersionCommand, "Icon.Info"));
        list.Add(VmCmd(L("Cmd_Cat_Build"), L("Cmd_PrintEnv"), _vm.Build.PrintTherionEnvironmentCommand, "Icon.Info"));
        // each export target parsed from the active thconfig.
        foreach (var target in _vm.Build.ExportTargets)
            list.Add(Action(L("Cmd_Cat_Build"), string.Format(L("Cmd_BuildTargetFmt"), target.Title),
                () => { target.BuildCommand.Execute(null); return Task.CompletedTask; }, "Icon.Cube"));
        if (_session is { Candidates.Count: > 0 })
            list.Add(Action(L("Cmd_Cat_Build"), L("Cmd_BuildSpecific"), () => { PushThconfigBuild(); return Task.CompletedTask; }, "Icon.Config"));

        // ---- View / panels ----
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleObjectBrowser"), _vm.ToggleObjectBrowserCommand, "Icon.Map"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleWorkspace"), _vm.ToggleWorkspaceExplorerCommand, "Icon.Folder"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleDiagnostics"), _vm.ToggleDiagnosticsCommand, "Icon.Search"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleOutline"), _vm.ToggleOutlineCommand, "Icon.Map"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleProject"), _vm.ToggleProjectCommand, "Icon.NodeGraph"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleLog"), _vm.ToggleLogCommand, "Icon.Search"));
        if (_vm.LivePreviewEnabled)
            list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleLivePreview"), _vm.ToggleLivePreviewCommand, "Icon.Map"));
        if (_vm.MapViewerEnabled)
            list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ToggleMapViewer"), _vm.ToggleMapViewerCommand, "Icon.Map"));
        if (_vm.Model3DViewerEnabled)
            list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_Toggle3D"), _vm.ToggleModel3DViewerCommand, "Icon.Cube"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Menu_View_SplitEditor"), _vm.SplitEditorCommand, "Icon.File"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Cmd_ResetLayout"), _vm.ResetLayoutCommand, "Icon.Folder"));
        list.Add(VmCmd(L("Cmd_Cat_View"), L("Layout_FloatActive"), _vm.FloatActiveDocumentCommand, "Icon.File"));

        // ---- Navigation / search ----
        list.Add(VmCmd(L("Cmd_Cat_Go"), L("Cmd_Back"), _vm.GoBackCommand, "Icon.Search"));
        list.Add(VmCmd(L("Cmd_Cat_Go"), L("Cmd_Forward"), _vm.GoForwardCommand, "Icon.Search"));
        list.Add(VmCmd(L("Cmd_Cat_Search"), L("Search_Title"), _vm.ShowFindInFilesCommand, "Icon.Search"));
        list.Add(VmCmd(L("Cmd_Cat_Search"), L("Replace_Title"), _vm.ShowReplaceInFilesCommand, "Icon.Search"));
        list.Add(VmCmd(L("Cmd_Cat_Edit"), L("Cmd_RenameSymbol"), _vm.RenameSymbolCommand, "Icon.File"));

        // ---- Symbols / identifiers (pushed sub-steps; search like the doc terms) ----
        list.Add(Action(L("Cmd_Cat_Go"), L("Cmd_GoToSymbolWs"), () => { PushSymbolSearch(workspace: true); return Task.CompletedTask; }, "Icon.Map"));
        list.Add(Action(L("Cmd_Cat_Go"), L("Cmd_GoToSymbolDoc"), () => { PushSymbolSearch(workspace: false); return Task.CompletedTask; }, "Icon.Map"));
        list.Add(Editor(L("Cmd_GoToLine"), ed => ed.MenuGoToLine()));

        // ---- Editor view toggles ----
        list.Add(Action(L("Cmd_Cat_View"), L("Cmd_ToggleWordWrap"), () => { _vm.WordWrap = !_vm.WordWrap; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action(L("Cmd_Cat_View"), L("Cmd_ToggleWhitespace"), () => { _vm.ShowWhitespace = !_vm.ShowWhitespace; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action(L("Cmd_Cat_View"), L("Cmd_ToggleMinimap"), () => { _vm.ShowMinimap = !_vm.ShowMinimap; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action(L("Cmd_Cat_View"), L("Cmd_ToggleIndent"), () => { _vm.ShowIndentGuides = !_vm.ShowIndentGuides; return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action(L("Cmd_Cat_View"), L("Cmd_ToggleEol"), () => { _vm.ShowEndOfLine = !_vm.ShowEndOfLine; return Task.CompletedTask; }, "Icon.File"));

        // ---- File extras ----
        if (_documents is not null)
            list.Add(Action(L("Cmd_Cat_File"), L("Cmd_RevealActive"), () =>
            {
                if (_documents.CurrentPath is { Length: > 0 } p) _documents.RequestSelectFileInWorkspace(p);
                return Task.CompletedTask;
            }, "Icon.Folder"));

        // ---- Language ----
        list.Add(VmCmd(L("Cmd_Cat_Language"), L("Cmd_SwitchEnglish"), _vm.SwitchToEnglishCommand, "Icon.Config"));
        list.Add(VmCmd(L("Cmd_Cat_Language"), L("Cmd_SwitchRomanian"), _vm.SwitchToRomanianCommand, "Icon.Config"));

        // ---- Editor actions (act on the focused editor) ----
        list.Add(Editor(L("Menu_Edit_Cut"), ed => ed.MenuCut()));
        list.Add(Editor(L("Menu_Edit_Copy"), ed => ed.MenuCopy()));
        list.Add(Editor(L("Menu_Edit_Paste"), ed => ed.MenuPaste()));
        list.Add(Editor(L("Menu_Edit_Delete"), ed => ed.MenuDelete()));
        list.Add(Editor(L("Menu_Edit_SelectAll"), ed => ed.MenuSelectAll()));
        list.Add(Editor(L("Cmd_UpperSel"), ed => ed.MenuUpperCase()));
        list.Add(Editor(L("Cmd_LowerSel"), ed => ed.MenuLowerCase()));
        list.Add(Editor(L("Menu_Edit_ToggleComment"), ed => ed.MenuToggleComment()));
        list.Add(Editor(L("Menu_Edit_FoldAll"), ed => ed.MenuFoldAll()));
        list.Add(Editor(L("Menu_Edit_UnfoldAll"), ed => ed.MenuUnfoldAll()));
        // line operations.
        list.Add(Editor(L("Ed_DuplicateLines"), ed => ed.DuplicateLines()));
        list.Add(Editor(L("Ed_MoveLinesUp"), ed => ed.MoveLinesUp()));
        list.Add(Editor(L("Ed_MoveLinesDown"), ed => ed.MoveLinesDown()));
        list.Add(Editor(L("Ed_SortLines"), ed => ed.SortSelectedLines()));
        // insert helpers.
        list.Add(Editor(L("Ed_InsertDate"), ed => ed.InsertDate()));
        list.Add(Editor(L("Ed_InsertTeamMember"), ed => ed.InsertTeamMember()));
        list.Add(Editor(L("Menu_Edit_AddBookmark"), ed => ed.MenuAddBookmark()));
        list.Add(Editor(L("Cmd_Find"), ed => ed.MenuFind()));
        list.Add(Editor(L("Cmd_Replace"), ed => ed.MenuReplace()));
        list.Add(Editor(L("Cmd_FormatDoc"), ed => ed.FormatDocument()));
        list.Add(Editor(L("Cmd_EncloseRegion"), ed => ed.MenuEncloseInRegion()));
        list.Add(Editor(L("Cmd_QuickFix"), ed => ed.ShowQuickFixes()));
        list.Add(Editor(L("Cmd_GoToMatching"), ed => ed.GoToMatchingBlock()));
        list.Add(Editor(L("Cmd_PeekDef"), ed => ed.PeekDefinition()));
        list.Add(Editor(L("Cmd_StepInto"), ed => ed.FollowIncludeUnderCaret()));
        list.Add(Editor(L("Cmd_RenameEditor"), ed => ed.StartRename()));

        // ---- Windows ----
        list.Add(Action(L("Cmd_Cat_Window"), L("Cmd_Preferences"), () => { _vm.RaiseShowPreferences(null); return Task.CompletedTask; }, "Icon.Config"));
        list.Add(Action(L("Cmd_Cat_Window"), L("Cmd_About"), () => { _vm.RaiseShowAbout(); return Task.CompletedTask; }, "Icon.Config"));
        list.Add(Action(L("Cmd_Cat_Window"), L("Cmd_TherionBook"), () => { _vm.RaiseShowThbook(); return Task.CompletedTask; }, "Icon.Map"));
        list.Add(Action(L("Cmd_Cat_Window"), L("Book_Title"), () => { _vm.RaiseShowBookmarks(); return Task.CompletedTask; }, "Icon.File"));
        list.Add(Action(L("Cmd_Cat_Window"), L("Tb_RelationalMap"), () => { _vm.RaiseShowRelationalMap(); return Task.CompletedTask; }, "Icon.Map"));

        // ---- Settings sections (open Preferences at that tab) ----
        foreach (var (id, titleKey) in SettingsSections)
            list.Add(Action(L("Cmd_Cat_Settings"), string.Format(L("Cmd_SettingsFmt"), L(titleKey)), () => { _vm.RaiseShowPreferences(id); return Task.CompletedTask; }, "Icon.Config"));

        // ---- Documentation ----
        list.Add(Action(L("Cmd_Cat_Docs"), L("Cmd_UserGuide"), () => { _vm.OpenUserGuideCommand.Execute(null); return Task.CompletedTask; }, "Icon.File"));
        // a parameterized command that pushes a focused term-search sub-step
        if (_docs is { IsAvailable: true })
            list.Add(Action(L("Cmd_Cat_Docs"), L("Cmd_SearchDocs"), () => { PushDocSearch(); return Task.CompletedTask; }, "Icon.Map"));

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
                Detail = L("Cmd_DocDetail"),
                IconKey = "Icon.Map",
                NameLower = t.ToLowerInvariant(),
                PathLower = "docs",
                Run = () => { _docs!.Open(t); return Task.CompletedTask; },
            })
            .ToList();
        _push(new QuickPickViewModel(L("Cmd_DocsTitle"), L("Cmd_SearchTermWm"), text => Filter(terms, text)));
    }

    // Pushes a second step listing project identifiers (surveys / stations / scraps / maps);
    // accepting one navigates to its declaration. Workspace mode aggregates across files.
    private void PushSymbolSearch(bool workspace)
    {
        var items = workspace ? (_wsSymbols ??= BuildSymbolItems(true)) : (_docSymbols ??= BuildSymbolItems(false));
        if (items.Count == 0)
            items = new List<QuickPickItem> { new() { Title = L("Cmd_NoSymbols") } };
        var title = workspace ? L("Cmd_SymWsTitle") : L("Cmd_SymDocTitle");
        _push(new QuickPickViewModel(title, L("Cmd_SymWm"), text => Filter(items, text)));
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
                    if (_documents is not null)
                        await _documents.ActivateThconfigAsync(c.FullPath).ConfigureAwait(true);
                    if (_vm.Build.BuildCommand.CanExecute(null)) _vm.Build.BuildCommand.Execute(null);
                },
            };
        }).ToList();
        _push(new QuickPickViewModel(L("Cmd_BuildThTitle"), L("Cmd_BuildThWm"), text => Filter(items, text)));
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
        // the live graph isn't built yet (or is being rebuilt) → fall back to the
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

    // Title holds the Strings.resx key for the Preferences section (localized at display time).
    private static readonly (string Id, string TitleKey)[] SettingsSections =
    {
        ("general", "Pref_General"), ("theme", "Pref_Theme"), ("editor", "Pref_Editor"),
        ("editorfeatures", "Pref_EditorFeatures"), ("performance", "Pref_Performance"),
        ("workspace", "Pref_Workspace"), ("build", "Pref_Build"), ("external", "Pref_External"),
        ("keyboard", "Pref_Keyboard"),
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
        Detail = L("Cmd_Cat_Editor"),
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
