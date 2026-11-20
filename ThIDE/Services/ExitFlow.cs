// The main window's close sequence, kept out of the view so it can be driven without a window. With
// unsaved documents a close is held while Save all / Don't save / Cancel is asked, and is then requested
// again; a second close request while the question is up waits for that answer. A running job that the
// close would cut off (a Blender render) is asked about first and stopped when the user lets the window
// close. An OS shutdown cannot wait for an answer, so it stops the job, goes ahead and keeps the
// crash-recovery copies of the unsaved buffers.

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
    private readonly Func<bool> _jobRunning;
    private readonly Func<Task<bool>> _confirmStopJobAsync;
    private readonly Action _stopJob;
    private bool _confirmed;
    private bool _promptOpen;

    /// <param name="hasUnsavedDocuments">True when an open document has unsaved changes.</param>
    /// <param name="confirmAsync">Asks Save all / Don't save / Cancel; true when the window may close.</param>
    /// <param name="closeWindow">Requests the close again once the user lets it go (re-enters <see cref="OnClosing"/>).</param>
    /// <param name="jobRunning">True while a job runs that closing would stop (a render); null for none.</param>
    /// <param name="confirmStopJobAsync">Asks whether to stop that job and close; true to go ahead.</param>
    /// <param name="stopJob">Stops the job at once.</param>
    public ExitFlow(Func<bool> hasUnsavedDocuments, Func<Task<bool>> confirmAsync, Action closeWindow,
        Func<bool>? jobRunning = null, Func<Task<bool>>? confirmStopJobAsync = null, Action? stopJob = null)
    {
        _hasUnsavedDocuments = hasUnsavedDocuments;
        _confirmAsync = confirmAsync;
        _closeWindow = closeWindow;
        _jobRunning = jobRunning ?? (() => false);
        _confirmStopJobAsync = confirmStopJobAsync ?? (() => Task.FromResult(true));
        _stopJob = stopJob ?? (() => { });
    }

    /// <summary>The window is about to close; <paramref name="osShutdown"/> when the OS is ending the session.</summary>
    public ExitStep OnClosing(bool osShutdown)
    {
        if (_confirmed) return ExitStep.Exit;
        bool unsaved = _hasUnsavedDocuments();
        if (!unsaved && !_jobRunning()) return ExitStep.Exit;
        if (osShutdown)
        {
            _stopJob();
            return unsaved ? ExitStep.ExitKeepingRecovery : ExitStep.Exit;
        }
        return _promptOpen ? ExitStep.Wait : ExitStep.Ask;
    }

    /// <summary>
    /// Asks about a running job and then the unsaved documents for a held close. When the user lets the
    /// window close (stop the job; the chosen save left nothing unsaved, or Don't save), the job is
    /// stopped and the close is requested again, which now goes ahead.
    /// </summary>
    public async Task AskAsync()
    {
        _promptOpen = true;
        bool close = true;
        try
        {
            if (_jobRunning()) close = await _confirmStopJobAsync().ConfigureAwait(true);
            if (close && _hasUnsavedDocuments()) close = await _confirmAsync().ConfigureAwait(true);
        }
        finally { _promptOpen = false; }
        if (!close) return;
        if (_jobRunning()) _stopJob();
        _confirmed = true;
        _closeWindow();
    }
}
