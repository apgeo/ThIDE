// In-app activity/diagnostics log (#3). A lightweight sink that app components write to (builds,
// thconfig/workspace changes, file-load errors, the startup load-timeout warning, …). The Log tool
// panel subscribes and renders entries at/above the user-selected verbosity. Separate from the
// Microsoft.Extensions.Logging plumbing the libraries use — this one is purely for the UI panel.

using System;

namespace TherionProc.Services;

/// <summary>Severity of an in-app log entry (verbose → most important).</summary>
public enum LogVerbosity
{
    Verbose = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>One in-app log entry.</summary>
public sealed record LogEntry(DateTimeOffset Time, LogVerbosity Level, string Message);

/// <summary>App-wide activity log sink. Raises <see cref="EntryAdded"/> for each appended entry.</summary>
public interface ILogService
{
    event EventHandler<LogEntry>? EntryAdded;
    void Log(LogVerbosity level, string message);
    void Verbose(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}

public sealed class LogService : ILogService
{
    public event EventHandler<LogEntry>? EntryAdded;

    public void Log(LogVerbosity level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message ?? string.Empty);
        EntryAdded?.Invoke(this, entry);
    }

    public void Verbose(string message) => Log(LogVerbosity.Verbose, message);
    public void Info(string message) => Log(LogVerbosity.Info, message);
    public void Warning(string message) => Log(LogVerbosity.Warning, message);
    public void Error(string message) => Log(LogVerbosity.Error, message);
}
