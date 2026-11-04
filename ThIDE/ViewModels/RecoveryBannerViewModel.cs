// The offer to restore the unsaved edits a crashed run left behind, pinned above the editor area until
// the user answers it. It names the files. The toast and its bell entry call the same commands, so only
// the first answer does anything. Discard asks first: the copies cannot be brought back.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThIDE.Resources;

namespace ThIDE.ViewModels;

public sealed partial class RecoveryBannerViewModel : ObservableObject
{
    private Func<Task>? _restore;
    private Action? _discard;
    private int _count;
    private bool _asking;

    /// <summary>The offer is shown and not yet answered.</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>"Unsaved changes … were kept for N file(s):".</summary>
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>The recovered files' names, comma-separated.</summary>
    [ObservableProperty] private string _files = string.Empty;

    /// <summary>The recovered files' full paths, one per line (the names' tooltip).</summary>
    [ObservableProperty] private string _paths = string.Empty;

    /// <summary>Asks before Discard deletes the copies (the view sets it; null discards without asking).</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    /// <summary>Shows the offer for the files at <paramref name="paths"/>.</summary>
    public void Offer(IReadOnlyList<string> paths, Func<Task> restore, Action discard)
    {
        _restore = restore;
        _discard = discard;
        _count = paths.Count;
        Message = Tr.Plural("Recovery_BannerMsg", paths.Count);
        Files = string.Join(", ", paths.Select(Path.GetFileName));
        Paths = string.Join(Environment.NewLine, paths);
        IsOpen = true;
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (!IsOpen || _restore is null) return;
        IsOpen = false;
        await _restore().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task Discard()
    {
        if (!IsOpen || _asking) return;
        if (Confirm is { } confirm)
        {
            _asking = true;
            try
            {
                if (!await confirm(Tr.Plural("Recovery_DiscardConfirm", _count, Files)).ConfigureAwait(true))
                    return;
            }
            finally { _asking = false; }
        }
        // Restore may have been chosen from the toast while the question was open.
        if (!IsOpen) return;
        IsOpen = false;
        _discard?.Invoke();
    }
}
