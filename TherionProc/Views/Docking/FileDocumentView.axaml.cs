using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Therion.Build;
using Therion.Core;
using Therion.Processing.Abstractions;
using TherionProc.Editor;
using TherionProc.Services;
using TherionProc.ViewModels;
using TherionProc.ViewModels.Docking;

namespace TherionProc.Views.Docking;

public partial class FileDocumentView : UserControl
{
    private FileDocumentViewModel? _vm;
    private FileSystemWatcher? _mapiahWatcher;
    private DateTime _lastMapiahReload = DateTime.MinValue;

    public FileDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            RestoreViewState();
            ApplyColumnVisibility();
        };
        DetachedFromVisualTree += (_, _) => DisposeMapiahWatcher();
        if (this.FindControl<TherionTextEditor>("Editor") is { } editor)
        {
            editor.OpenFileRequested += OnOpenFileRequested;
            editor.OpenExternalRequested += OnOpenExternalRequested;
            editor.NavigateToSpanRequested += OnNavigateToSpanRequested;
            editor.CaretMoved += OnCaretMoved;
            editor.HoverTargetChanged += OnHoverTargetChanged;
            editor.FindReferencesRequested += OnFindReferencesRequested;
            editor.RenameSymbolRequested += OnRenameSymbolRequested;
            editor.StepOutRequested += OnStepOutRequested; // EDIT-17
        }
    }

    private void OnFindReferencesRequested(object? sender, string term)
        => TryDocuments()?.RequestFindReferences(term);

    // EDIT-17: step back out of an included file to whoever opened it (reuses the nav history).
    private void OnStepOutRequested(object? sender, EventArgs e)
        => _ = TryDocuments()?.GoBackAsync();

    // Reveal-in-workspace button (#1): ask the Workspace Explorer to switch to file view
    // and select/expand to this document's file (only if it lives under the workspace root).
    private void OnSelectInWorkspace(object? sender, RoutedEventArgs e)
    {
        if (_vm?.FilePath is { } path && !string.IsNullOrEmpty(path))
            TryDocuments()?.RequestSelectFileInWorkspace(path);
    }

    // Open-files dropdown (#15): populate the flyout with every open document; clicking one
    // re-activates its tab.
    private void OnShowOpenFiles(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Flyout: MenuFlyout flyout }) return;
        if (TryDocuments() is not { } docs) return;

        var items = new System.Collections.Generic.List<Control>();
        foreach (var doc in docs.Documents)
        {
            var path = doc.FilePath;
            var item = new MenuItem
            {
                Header = System.IO.Path.GetFileName(path),
                [ToolTip.TipProperty] = path,
                Icon = ReferenceEquals(doc, docs.Active)
                    ? new TextBlock { Text = "•" } // bullet marks the active document
                    : null,
            };
            item.Click += (_, _) => _ = docs.OpenFileAsync(path);
            items.Add(item);
        }
        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(no open files)", IsEnabled = false });
        flyout.ItemsSource = items;
    }

    private void OnRenameSymbolRequested(object? sender, (string Raw, Therion.Processing.Abstractions.ReferenceKind Kind) args)
        => TryDocuments()?.RequestRenameSymbol(args.Raw, args.Kind);

    // Ask the Workspace Explorer to reveal the hovered link's target (gated by its toggle, #8).
    private void OnHoverTargetChanged(object? sender, SourceSpan? target)
    {
        if (target is { } span) TryDocuments()?.RequestRevealInWorkspace(span);
    }

    // An export/output link (.lox/.pdf/.3d/...) — open in the OS default viewer (#15)
    // through the cross-platform IShellOpener (Windows ShellExecute / macOS `open` /
    // Linux `xdg-open`), rather than a raw Process.Start whose UseShellExecute=true only
    // resolves a default handler reliably on Windows.
    private void OnOpenExternalRequested(object? sender, string path)
        => TryShellOpener()?.Open(path);

    private void OnCaretMoved(object? sender, SourceSpan span)
    {
        if (_vm is null) return;
        _vm.SetCaret(span);
        _vm.SavedCaretOffset = span.StartOffset; // remember position for tab switches (#11)

        // Feed the caret into the back/forward history, but skip moves driven by
        // highlighted-term navigation (Shift+F12 cycling), per #1. Pointer clicks are
        // recorded even for short moves so back/forward tracks all visited points (#8).
        var ed = sender as TherionTextEditor;
        // QOL-04: find-next moves from the search panel are excluded from the trail too.
        bool termNav = (ed?.IsTermNavigating ?? false) || (ed?.LastCaretMoveFromSearch ?? false);
        bool fromPointer = ed?.LastCaretMoveFromPointer ?? false;
        TryDocuments()?.ReportCaret(span, termNav, fromPointer);
    }

    // Restore the caret when this tab is shown again — unless a navigation scroll is pending.
    private void RestoreViewState()
    {
        if (_vm is null || _vm.PendingScroll is not null || _vm.SavedCaretOffset <= 0) return;
        var offset = _vm.SavedCaretOffset;
        Dispatcher.UIThread.Post(
            () => this.FindControl<TherionTextEditor>("Editor")?.RestoreCaret(offset),
            DispatcherPriority.Loaded);
    }

    private void OnOpenFileRequested(object? sender, string path)
    {
        // The editor resolved an input/load target; open it as a document.
        _ = TryDocuments()?.OpenFileAsync(path);
    }

    private void OnNavigateToSpanRequested(object? sender, SourceSpan span)
    {
        // A cross-file reference resolved into another file — open it and scroll/flash.
        _ = TryDocuments()?.NavigateToSpanAsync(span);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm?.Measurements is { } oldMvm)
            oldMvm.PropertyChanged -= OnMeasurementsPropertyChanged;

        if (_vm is not null)
        {
            _vm.ScrollToSpanRequested -= OnScrollRequested;
            _vm.OpenLimitSettingsRequested -= OnOpenLimitSettings;
            _vm.SaveCleanupRequested -= OnSaveCleanupRequested;
            _vm.CompareExternalRequested -= OnCompareExternalRequested;
        }
        DisposeMapiahWatcher(); // the watcher is tied to the previous document's file
        _vm = DataContext as FileDocumentViewModel;
        if (_vm is not null)
        {
            _vm.ScrollToSpanRequested += OnScrollRequested;
            _vm.OpenLimitSettingsRequested += OnOpenLimitSettings;
            _vm.SaveCleanupRequested += OnSaveCleanupRequested;
            _vm.CompareExternalRequested += OnCompareExternalRequested;
            ApplyPendingScrollDeferred();
            if (_vm.Measurements is { } mvm)
                mvm.PropertyChanged += OnMeasurementsPropertyChanged;
        }
    }

    // TRUST-05: "Compare" on the external-change banner → read-only disk-vs-editor diff.
    private void OnCompareExternalRequested(object? sender, string diskText)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        new DiffDialog($"Compare — {System.IO.Path.GetFileName(_vm.FilePath)}",
            "On disk", diskText, "In editor (unsaved)", _vm.DocumentText).ShowDialog(owner);
    }

    // Large-file banner "Settings…" button (#10): open Preferences at the Performance section.
    private async void OnOpenLimitSettings(object? sender, EventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        IAppSettingsService? settings = null;
        ViewModels.KeyboardShortcutsViewModel? keyboard = null;
        IServiceProvider? sp = null;
        try { sp = AppServices.Provider; } catch { return; }
        settings = sp.GetService<IAppSettingsService>();
        keyboard = sp.GetService<ViewModels.KeyboardShortcutsViewModel>();
        var language = sp.GetService<ILanguageService>();
        var externalTools = sp.GetService<ViewModels.SettingsViewModel>();
        if (settings is null) return;

        var vm = new ViewModels.PreferencesViewModel(settings, keyboard, language, externalTools);
        vm.SelectSectionById("performance");
        await new PreferencesWindow { DataContext = vm }.ShowDialog(owner);
    }

    private void OnScrollRequested(object? sender, SourceSpan span)
    {
        this.FindControl<TherionTextEditor>("Editor")?.ScrollTo(span);
        _vm?.ClearPendingScroll();
    }

    // EDIT-14: clean the document in place before the shell writes it, so the caret is preserved.
    private void OnSaveCleanupRequested(object? sender, EventArgs e)
        => this.FindControl<TherionTextEditor>("Editor")?.ApplySaveCleanup();

    // A document opened via navigation may bind its view after the scroll was requested;
    // replay the pending target once the editor is laid out.
    private void ApplyPendingScrollDeferred()
    {
        if (_vm?.PendingScroll is not { } span) return;
        Dispatcher.UIThread.Post(() =>
        {
            this.FindControl<TherionTextEditor>("Editor")?.ScrollTo(span);
            _vm?.ClearPendingScroll();
        }, DispatcherPriority.Loaded);
    }

    // ---- Column visibility ----

    private void OnMeasurementsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm?.Measurements is not { } mvm) return;
        var g = this.FindControl<DataGrid>("ShotsGrid");
        if (g is not null && g.Columns.Count == 12)
        {
            switch (e.PropertyName)
            {
                case nameof(MeasurementsViewModel.ShotsColSurvey):     g.Columns[0].IsVisible  = mvm.ShotsColSurvey;     break;
                case nameof(MeasurementsViewModel.ShotsColFrom):        g.Columns[1].IsVisible  = mvm.ShotsColFrom;        break;
                case nameof(MeasurementsViewModel.ShotsColTo):          g.Columns[2].IsVisible  = mvm.ShotsColTo;          break;
                case nameof(MeasurementsViewModel.ShotsColLength):      g.Columns[3].IsVisible  = mvm.ShotsColLength;      break;
                case nameof(MeasurementsViewModel.ShotsColCompass):     g.Columns[4].IsVisible  = mvm.ShotsColCompass;     break;
                case nameof(MeasurementsViewModel.ShotsColClino):       g.Columns[5].IsVisible  = mvm.ShotsColClino;       break;
                case nameof(MeasurementsViewModel.ShotsColSurface):     g.Columns[6].IsVisible  = mvm.ShotsColSurface;     break;
                case nameof(MeasurementsViewModel.ShotsColDuplicate):   g.Columns[7].IsVisible  = mvm.ShotsColDuplicate;   break;
                case nameof(MeasurementsViewModel.ShotsColSplay):       g.Columns[8].IsVisible  = mvm.ShotsColSplay;       break;
                case nameof(MeasurementsViewModel.ShotsColApproximate): g.Columns[9].IsVisible  = mvm.ShotsColApproximate; break;
                case nameof(MeasurementsViewModel.ShotsColComment):     g.Columns[10].IsVisible = mvm.ShotsColComment;     break;
                case nameof(MeasurementsViewModel.ShotsColLine):        g.Columns[11].IsVisible = mvm.ShotsColLine;        break;
            }
        }
        var sg = this.FindControl<DataGrid>("StationsGrid");
        if (sg is not null && sg.Columns.Count == 4)
        {
            switch (e.PropertyName)
            {
                case nameof(MeasurementsViewModel.StationsColSurvey): sg.Columns[1].IsVisible = mvm.StationsColSurvey; break;
                case nameof(MeasurementsViewModel.StationsColKind):   sg.Columns[2].IsVisible = mvm.StationsColKind;   break;
                case nameof(MeasurementsViewModel.StationsColLine):   sg.Columns[3].IsVisible = mvm.StationsColLine;   break;
            }
        }
    }

    private void ApplyColumnVisibility()
    {
        if (_vm?.Measurements is not { } mvm) return;
        var g = this.FindControl<DataGrid>("ShotsGrid");
        if (g is not null && g.Columns.Count == 12)
        {
            g.Columns[0].IsVisible  = mvm.ShotsColSurvey;
            g.Columns[1].IsVisible  = mvm.ShotsColFrom;
            g.Columns[2].IsVisible  = mvm.ShotsColTo;
            g.Columns[3].IsVisible  = mvm.ShotsColLength;
            g.Columns[4].IsVisible  = mvm.ShotsColCompass;
            g.Columns[5].IsVisible  = mvm.ShotsColClino;
            g.Columns[6].IsVisible  = mvm.ShotsColSurface;
            g.Columns[7].IsVisible  = mvm.ShotsColDuplicate;
            g.Columns[8].IsVisible  = mvm.ShotsColSplay;
            g.Columns[9].IsVisible  = mvm.ShotsColApproximate;
            g.Columns[10].IsVisible = mvm.ShotsColComment;
            g.Columns[11].IsVisible = mvm.ShotsColLine;
        }
        var sg = this.FindControl<DataGrid>("StationsGrid");
        if (sg is not null && sg.Columns.Count == 4)
        {
            sg.Columns[1].IsVisible = mvm.StationsColSurvey;
            sg.Columns[2].IsVisible = mvm.StationsColKind;
            sg.Columns[3].IsVisible = mvm.StationsColLine;
        }
    }

    // ---- Auto-fit handlers ----

    private void OnFitShotsColumns(object? sender, RoutedEventArgs e)
    {
        var g = this.FindControl<DataGrid>("ShotsGrid");
        if (g is null) return;
        foreach (var col in g.Columns)
            col.Width = DataGridLength.Auto;
    }

    private void OnFitStationsColumns(object? sender, RoutedEventArgs e)
    {
        var g = this.FindControl<DataGrid>("StationsGrid");
        if (g is null) return;
        foreach (var col in g.Columns)
            col.Width = DataGridLength.Auto;
    }

    // ---- Shots context menu ----

    private MeasurementRow? ShotGridSelectedRow()
        => this.FindControl<DataGrid>("ShotsGrid")?.SelectedItem as MeasurementRow;

    private DataGridColumn? ShotGridCurrentColumn()
        => this.FindControl<DataGrid>("ShotsGrid")?.CurrentColumn;

    private void OnCopyShotRow(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        var text = string.Join("\t",
            row.Survey, row.From, row.To,
            row.Length?.ToString() ?? "", row.Compass?.ToString() ?? "", row.Clino?.ToString() ?? "",
            row.Surface ? "1" : "0", row.Duplicate ? "1" : "0",
            row.Splay ? "1" : "0", row.Approximate ? "1" : "0",
            row.Comment ?? "", row.Line.ToString());
        CopyToClipboard(text);
    }

    private void OnCopyShotCell(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        var text = GetShotCellValue(row, ShotGridCurrentColumn());
        if (text is null) return;
        CopyToClipboard(text);
    }

    private void OnGoToShotSource(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        GoToLine(row.Line);
    }

    private void OnGoToShotDefFrom(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        GoToDefinition(row.FromFull);
    }

    private void OnGoToShotDefTo(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        GoToDefinition(row.ToFull);
    }

    private void OnRenameFromStation(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        TryDocuments()?.RequestRenameSymbol(row.FromShort, ReferenceKind.Station);
    }

    private void OnRenameToStation(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is not { } row) return;
        TryDocuments()?.RequestRenameSymbol(row.ToShort, ReferenceKind.Station);
    }

    private static string? GetShotCellValue(MeasurementRow row, DataGridColumn? col)
    {
        if (col is null) return null;
        return col.Header?.ToString() switch
        {
            "Survey"  => row.Survey,
            "From"    => row.From,
            "To"      => row.To,
            "Length"  => row.Length?.ToString(),
            "Compass" => row.Compass?.ToString(),
            "Clino"   => row.Clino?.ToString(),
            "Surf"    => row.Surface     ? "1" : "0",
            "Dup"     => row.Duplicate   ? "1" : "0",
            "Splay"   => row.Splay       ? "1" : "0",
            "Approx"  => row.Approximate ? "1" : "0",
            "Comment" => row.Comment,
            "Line"    => row.Line.ToString(),
            _         => null,
        };
    }

    // ---- Stations context menu ----

    private StationMeasurementRow? StationGridSelectedRow()
        => this.FindControl<DataGrid>("StationsGrid")?.SelectedItem as StationMeasurementRow;

    private DataGridColumn? StationGridCurrentColumn()
        => this.FindControl<DataGrid>("StationsGrid")?.CurrentColumn;

    private void OnCopyStationRow(object? sender, RoutedEventArgs e)
    {
        if (StationGridSelectedRow() is not { } row) return;
        var text = string.Join("\t", row.Name, row.Survey, row.Kind, row.Line.ToString());
        CopyToClipboard(text);
    }

    private void OnCopyStationCell(object? sender, RoutedEventArgs e)
    {
        if (StationGridSelectedRow() is not { } row) return;
        var text = GetStationCellValue(row, StationGridCurrentColumn());
        if (text is null) return;
        CopyToClipboard(text);
    }

    private void OnGoToStationSource(object? sender, RoutedEventArgs e)
    {
        if (StationGridSelectedRow() is not { } row) return;
        GoToLine(row.Line);
    }

    private void OnGoToStationDeclaration(object? sender, RoutedEventArgs e)
    {
        if (StationGridSelectedRow() is not { } row) return;
        GoToDefinition(row.FullName);
    }

    private void OnRenameStation(object? sender, RoutedEventArgs e)
    {
        if (StationGridSelectedRow() is not { } row) return;
        TryDocuments()?.RequestRenameSymbol(row.ShortName, ReferenceKind.Station);
    }

    // VIS-01: reveal the selected station/shot endpoint in the embedded 3D viewer.
    private void OnShowStationIn3D(object? sender, RoutedEventArgs e)
    {
        if (StationGridSelectedRow() is { } row) TryDocuments()?.RequestShowInModel3D(row.FullName);
    }

    private void OnShowShotFromIn3D(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is { } row) TryDocuments()?.RequestShowInModel3D(row.FromFull);
    }

    private void OnShowShotToIn3D(object? sender, RoutedEventArgs e)
    {
        if (ShotGridSelectedRow() is { } row) TryDocuments()?.RequestShowInModel3D(row.ToFull);
    }

    private static string? GetStationCellValue(StationMeasurementRow row, DataGridColumn? col)
    {
        if (col is null) return null;
        return col.Header?.ToString() switch
        {
            "Station" => row.Name,
            "Survey"  => row.Survey,
            "Kind"    => row.Kind,
            "Line"    => row.Line.ToString(),
            _         => null,
        };
    }

    // ---- Navigation helpers ----

    private void GoToLine(int line)
    {
        // Switch to Source tab and scroll to the line.
        if (this.FindControl<TabControl>("DocumentTabControl") is { } tc)
            tc.SelectedIndex = 0;
        Dispatcher.UIThread.Post(
            () => this.FindControl<TherionTextEditor>("Editor")?.ScrollTo(line, 1),
            DispatcherPriority.Loaded);
    }

    private void GoToDefinition(string qualifiedName)
    {
        try
        {
            var nav = AppServices.Provider.GetService<ISymbolNavigationService>();
            var span = nav?.GoToDefinition(qualifiedName, ReferenceKind.Station);
            if (span is not { } s || s.IsEmpty) return;

            // Same file → switch to Source tab and scroll.
            if (string.Equals(s.FilePath, _vm?.FilePath, StringComparison.OrdinalIgnoreCase))
                GoToLine(s.Start.Line);
            else
                _ = TryDocuments()?.NavigateToSpanAsync(s);
        }
        catch { }
    }

    private void CopyToClipboard(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
    }

    // ---- Edit with Mapiah (external .th2 sketch editor) ------------------

    private async void OnEditWithMapiah(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || !_vm.IsTh2File) return;

        var owner = TopLevel.GetTopLevel(this) as Window;

        // Persist any pending edits so Mapiah opens the latest text. Suppress the self-write
        // so our own save doesn't raise the "changed on disk" banner.
        if (_vm.IsDirty)
        {
            try
            {
                TrySession()?.SuppressSelfWrite(_vm.FilePath);
                await File.WriteAllTextAsync(_vm.FilePath, _vm.DocumentText).ConfigureAwait(true);
                _vm.SetText(_vm.DocumentText, reparse: false);
            }
            catch { /* fall through — let Mapiah open whatever is on disk */ }
        }

        var mapiah = TryMapiah();
        if (mapiah is null) return;

        var result = await mapiah.EditAsync(_vm.FilePath).ConfigureAwait(true);
        switch (result.Status)
        {
            case MapiahLaunchStatus.Launched:
                StartMapiahWatch(_vm.FilePath);
                _vm.InfoBanner = "Opened in Mapiah — saved changes will reload here automatically.";
                break;
            case MapiahLaunchStatus.NotInstalled:
                if (owner is not null) await HandleMapiahNotFoundAsync(owner);
                break;
            case MapiahLaunchStatus.LaunchFailed:
                _vm.InfoBanner = "Could not launch Mapiah: " + (result.Error ?? "unknown error");
                break;
        }
    }

    private async System.Threading.Tasks.Task HandleMapiahNotFoundAsync(Window owner)
    {
        var choice = await new MapiahNotFoundDialog().ShowAsync(owner);
        switch (choice)
        {
            case MapiahNotFoundChoice.Download:
                TryShellOpener()?.Open(MapiahLinks.Releases);
                break;
            case MapiahNotFoundChoice.OpenSettings:
                (owner.DataContext as MainWindowViewModel)?.ToggleSettingsCommand.Execute(null);
                break;
        }
    }

    // Watch the .th2 on disk while it's open in Mapiah: when Mapiah saves, reload it into this
    // tab (silently when clean, via the standard external-change banner when we have edits).
    private void StartMapiahWatch(string path)
    {
        if (_mapiahWatcher is not null) return;
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file) || !Directory.Exists(dir)) return;

        try
        {
            var w = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            w.Changed += OnMapiahFileChanged;
            w.Created += OnMapiahFileChanged;
            _mapiahWatcher = w;
        }
        catch { /* best-effort */ }
    }

    private void OnMapiahFileChanged(object? sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher fires multiple times per save — debounce to one reload per burst.
        var now = DateTime.UtcNow;
        if (now - _lastMapiahReload < TimeSpan.FromMilliseconds(400)) return;
        _lastMapiahReload = now;
        Dispatcher.UIThread.Post(() =>
            _vm?.NotifyExternalChange(DateTime.Now, deleted: false, autoReload: true));
    }

    private void DisposeMapiahWatcher()
    {
        if (_mapiahWatcher is null) return;
        try
        {
            _mapiahWatcher.Changed -= OnMapiahFileChanged;
            _mapiahWatcher.Created -= OnMapiahFileChanged;
            _mapiahWatcher.Dispose();
        }
        catch { }
        _mapiahWatcher = null;
    }

    private static IMapiahService? TryMapiah()
    {
        try { return AppServices.Provider.GetService<IMapiahService>(); }
        catch { return null; }
    }

    private static IWorkspaceSession? TrySession()
    {
        try { return AppServices.Provider.GetService<IWorkspaceSession>(); }
        catch { return null; }
    }

    // ---- Service helpers ----

    private static IDocumentService? TryDocuments()
    {
        try { return AppServices.Provider.GetService<IDocumentService>(); }
        catch { return null; } // design-time / no container
    }

    private static IShellOpener? TryShellOpener()
    {
        try { return AppServices.Provider.GetService<IShellOpener>(); }
        catch { return null; } // design-time / no container
    }
}
