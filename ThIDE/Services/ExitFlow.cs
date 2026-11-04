// The main window's close sequence, kept out of the view so it can be driven without a window. With
// unsaved documents a close is held while Save all / Don't save / Cancel is asked, and is then requested
// again; a second close request while the question is up waits for that answer. An OS shutdown cannot
// wait for an answer, so it goes ahead and keeps the crash-recovery copies of the unsaved buffers.

using System;
using System.Threading.Tasks;

namespace ThIDE.Services;

/// <summary>What the window does with a close request (see <see cref="ExitFlow.OnClosing"/>).</summary>
public enum ExitStep
{
    /// <summary>Cancel the close and ask with <see cref="ExitFlow.AskAsync"/>.</summary>
    Ask,
    /// <summary>Cancel the close: the question asked for an earlier request is still open.</summary>
    Wait,
    /// <summary>Shut down; nothing unsaved is left, so the run's recovery copies go.</summary>
    Exit,
    /// <summary>Shut down without asking (OS shutdown): write the unsaved buffers' recovery copies and keep them.</summary>
    ExitKeepingRecovery,
}

public sealed class ExitFlow
{
    private readonly Func<bool> _hasUnsavedDocuments;
    private readonly Func<Task<bool>> _confirmAsync;
    private readonly Action _closeWindow;
    private bool _confirmed;
    private bool _promptOpen;

    /// <param name="hasUnsavedDocuments">True when an open document has unsaved changes.</param>
    /// <param name="confirmAsync">Asks Save all / Don't save / Cancel; true when the window may close.</param>
    /// <param name="closeWindow">Requests the close again once the user lets it go (re-enters <see cref="OnClosing"/>).</param>
    public ExitFlow(Func<bool> hasUnsavedDocuments, Func<Task<bool>> confirmAsync, Action closeWindow)
    {
        _hasUnsavedDocuments = hasUnsavedDocuments;
        _confirmAsync = confirmAsync;
        _closeWindow = closeWindow;
    }

    /// <summary>The window is about to close; <paramref name="osShutdown"/> when the OS is ending the session.</summary>
    public ExitStep OnClosing(bool osShutdown)
    {
        if (_confirmed || !_hasUnsavedDocuments()) return ExitStep.Exit;
        if (osShutdown) return ExitStep.ExitKeepingRecovery;
        return _promptOpen ? ExitStep.Wait : ExitStep.Ask;
    }

    /// <summary>
    /// Asks about the unsaved documents for a held close. When the user lets the window close (the chosen
    /// save left nothing unsaved, or Don't save), the close is requested again and now goes ahead.
    /// </summary>
    public async Task AskAsync()
    {
        _promptOpen = true;
        bool close;
        try { close = await _confirmAsync().ConfigureAwait(true); }
        finally { _promptOpen = false; }
        if (!close) return;
        _confirmed = true;
        _closeWindow();
    }
}
