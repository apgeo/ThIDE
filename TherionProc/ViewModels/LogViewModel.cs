// #3 — Log panel content VM. Accumulates a read-only running text of activity-log entries that
// meet the selected verbosity threshold AT THE TIME they arrive. Changing the threshold only
// affects subsequent entries (no re-filter of already-shown text), per spec.

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TherionProc.Services;

namespace TherionProc.ViewModels;

public sealed partial class LogViewModel : ObservableObject
{
    private readonly StringBuilder _buffer = new();

    /// <summary>The verbosity choices shown in the selector (verbose → most important).</summary>
    public IReadOnlyList<LogVerbosity> Levels { get; } = new[]
    {
        LogVerbosity.Verbose, LogVerbosity.Info, LogVerbosity.Warning, LogVerbosity.Error,
    };

    /// <summary>Minimum level for <em>new</em> entries to be shown. Default: Info (hides verbose noise).</summary>
    [ObservableProperty] private LogVerbosity _minLevel = LogVerbosity.Info;

    /// <summary>The running read-only log text.</summary>
    [ObservableProperty] private string _text = string.Empty;

    public LogViewModel() { } // design-time

    public LogViewModel(ILogService log)
    {
        log.EntryAdded += (_, e) => OnUi(() => Append(e));
    }

    private void Append(LogEntry entry)
    {
        // Threshold is applied once, at arrival; lowering it later does not resurrect dropped lines.
        if (entry.Level < MinLevel) return;
        _buffer.Append(entry.Time.ToString("HH:mm:ss"))
               .Append("  ").Append(Tag(entry.Level))
               .Append("  ").Append(entry.Message)
               .Append('\n');
        Text = _buffer.ToString();
    }

    [RelayCommand]
    private void Clear()
    {
        _buffer.Clear();
        Text = string.Empty;
    }

    private static string Tag(LogVerbosity level) => level switch
    {
        LogVerbosity.Error => "[ERROR]",
        LogVerbosity.Warning => "[WARN ]",
        LogVerbosity.Info => "[INFO ]",
        _ => "[VERB ]",
    };

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
