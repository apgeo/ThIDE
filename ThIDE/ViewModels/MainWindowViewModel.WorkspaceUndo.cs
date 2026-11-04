// Edit ▸ Undo <operation> / Redo <operation>: the workspace-level history of multi-file writes
// (rename, Replace in Files, New trip, Link survey, Renumber, Format all, Normalise). Every recorded
// operation is announced in a notification naming the files it changed, with an Undo button.

using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.ViewModels;

public partial class MainWindowViewModel
{
    private IWorkspaceUndoService? _workspaceUndo;

    /// <summary>"Undo Rename", or a disabled "Undo Multi-File Change" when there is nothing to undo.</summary>
    public string WorkspaceUndoHeader => _workspaceUndo?.NextUndo is { } r
        ? string.Format(L("WsUndo_Menu", "Undo {0}"), r.Label)
        : L("WsUndo_MenuNone", "Undo Multi-File Change");

    /// <summary>"Redo Rename", or a disabled "Redo Multi-File Change".</summary>
    public string WorkspaceRedoHeader => _workspaceUndo?.NextRedo is { } r
        ? string.Format(L("WsUndo_RedoMenu", "Redo {0}"), r.Label)
        : L("WsUndo_RedoMenuNone", "Redo Multi-File Change");

    public bool CanUndoWorkspace => _workspaceUndo?.NextUndo is not null;
    public bool CanRedoWorkspace => _workspaceUndo?.NextRedo is not null;

    private void AttachWorkspaceUndo(IWorkspaceUndoService? history)
    {
        _workspaceUndo = history;
        if (history is null) return;
        history.Changed += (_, _) => OnUiThread(RefreshWorkspaceUndo);
        // The operation's notification names the files it changed and offers the undo.
        history.Pushed += (_, record) => _notifications.Post(new AppNotification(
            record.Label,
            record.Summary + "\n" + Tr.Plural("WsUndo_Files", record.Files.Count, record.FileNames()),
            NotificationKind.Success,
            L("Common_Undo", "Undo"),
            () => _ = UndoWorkspaceRecordAsync(record)));
    }

    private void RefreshWorkspaceUndo()
    {
        OnPropertyChanged(nameof(WorkspaceUndoHeader));
        OnPropertyChanged(nameof(WorkspaceRedoHeader));
        OnPropertyChanged(nameof(CanUndoWorkspace));
        OnPropertyChanged(nameof(CanRedoWorkspace));
        UndoWorkspaceCommand.NotifyCanExecuteChanged();
        RedoWorkspaceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Edit ▸ Undo &lt;operation&gt;: reverts the latest multi-file operation in every file it wrote.</summary>
    [RelayCommand(CanExecute = nameof(CanUndoWorkspace))]
    private Task UndoWorkspace() => UndoWorkspaceRecordAsync(null);

    /// <summary>Edit ▸ Redo &lt;operation&gt;: applies the latest undone operation again.</summary>
    [RelayCommand(CanExecute = nameof(CanRedoWorkspace))]
    private async Task RedoWorkspace()
    {
        if (_workspaceUndo is null) return;
        Report(await _workspaceUndo.RedoAsync().ConfigureAwait(true));
    }

    /// <summary>Reverts <paramref name="record"/> (the latest when null), e.g. from its notification's Undo button.</summary>
    public async Task<WorkspaceUndoOutcome?> UndoWorkspaceRecordAsync(WorkspaceUndoRecord? record)
    {
        if (_workspaceUndo is null) return null;
        var outcome = await _workspaceUndo.UndoAsync(record).ConfigureAwait(true);
        Report(outcome);
        return outcome;
    }

    private void Report(WorkspaceUndoOutcome outcome)
    {
        var title = outcome.Record?.Label ?? L("WsUndo_MenuNone", "Undo Multi-File Change");
        switch (outcome.Status)
        {
            case WorkspaceUndoStatus.Done: _notifications.Info(title, outcome.Message ?? string.Empty); break;
            case WorkspaceUndoStatus.Nothing: _notifications.Info(title, L("WsUndo_Gone", "This change was already undone.")); break;
            default: _notifications.Warning(title, outcome.Message ?? string.Empty); break;
        }
    }
}
