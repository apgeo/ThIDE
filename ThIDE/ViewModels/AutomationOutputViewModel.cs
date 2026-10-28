using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ThIDE.ViewModels;

public enum AutomationKind { Hook, Task, Macro, BatchBuild }

public enum AutomationState { Running, Succeeded, Failed, Cancelled }

/// <summary>One line of output; <see cref="File"/>/<see cref="Line"/> are set when it names a source position.</summary>
public sealed record AutomationRow(DateTime Time, string Text, bool IsError, string? File = null, int Line = 0);

/// <summary>Writes into one session of the Automation panel. Safe from any thread.</summary>
public interface IAutomationSessionWriter
{
    void Write(string text, bool isError = false);
    void Complete(AutomationState state, int? exitCode = null);
    /// <summary>Cancels what the session runs (wired by its owner).</summary>
    void OnCancel(Action cancel);
}

/// <summary>Where hooks, tasks, macros and batch builds report.</summary>
public interface IAutomationOutput
{
    IAutomationSessionWriter BeginSession(string title, AutomationKind kind);
    /// <summary>Raised when a session starts, so the shell can show the panel.</summary>
    event EventHandler? SessionStarted;
}

/// <summary>One run: its rows and how it ended.</summary>
public sealed partial class AutomationSession : ObservableObject, IAutomationSessionWriter
{
    private Action? _cancel;

    public AutomationSession(string title, AutomationKind kind)
    {
        Title = title;
        Kind = kind;
        StartedAt = DateTime.Now;
    }

    public string Title { get; }
    public AutomationKind Kind { get; }
    public DateTime StartedAt { get; }
    public ObservableCollection<AutomationRow> Rows { get; } = new();

    [ObservableProperty] private AutomationState _state = AutomationState.Running;
    [ObservableProperty] private int? _exitCode;

    public string Header => $"{StartedAt:HH:mm:ss}  {Title}";
    public bool IsRunning => State == AutomationState.Running;
    partial void OnStateChanged(AutomationState value) => OnPropertyChanged(nameof(IsRunning));

    // `path/to/file.th:12` or `file.th(12)` or Therion's `[file.th:12]` — the file must look like a path with an extension.
    private static readonly Regex Position = new(@"(?<file>[^\s:\[\]()""']+\.[A-Za-z0-9]+)(?::|\()(?<line>\d+)", RegexOptions.CultureInvariant);

    public void Write(string text, bool isError = false)
    {
        var row = ParseRow(text, isError);
        Post(() => Rows.Add(row));
    }

    /// <summary>A row, with the file and line it names when it names one.</summary>
    public static AutomationRow ParseRow(string text, bool isError)
    {
        var m = Position.Match(text);
        return m.Success && int.TryParse(m.Groups["line"].Value, out var line)
            ? new AutomationRow(DateTime.Now, text, isError, m.Groups["file"].Value, line)
            : new AutomationRow(DateTime.Now, text, isError);
    }

    public void Complete(AutomationState state, int? exitCode = null) => Post(() => { State = state; ExitCode = exitCode; });

    public void OnCancel(Action cancel) => _cancel = cancel;

    public void Cancel() => _cancel?.Invoke();

    public string Text() => string.Join(Environment.NewLine, Rows.Select(r => r.Text));

    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}

/// <summary>The Automation panel: sessions newest first, the selected one's rows below.</summary>
public sealed partial class AutomationOutputViewModel : ObservableObject, IAutomationOutput
{
    private const int MaxSessions = 50;

    public ObservableCollection<AutomationSession> Sessions { get; } = new();
    [ObservableProperty] private AutomationSession? _selected;

    public event EventHandler? SessionStarted;

    /// <summary>Raised to open a <c>file:line</c> the user clicked.</summary>
    public event EventHandler<AutomationRow>? NavigateRequested;

    public IAutomationSessionWriter BeginSession(string title, AutomationKind kind)
    {
        var session = new AutomationSession(title, kind);
        void Add()
        {
            Sessions.Insert(0, session);
            while (Sessions.Count > MaxSessions) Sessions.RemoveAt(Sessions.Count - 1);
            Selected = session;
            SessionStarted?.Invoke(this, EventArgs.Empty);
        }
        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);
        return session;
    }

    [RelayCommand]
    private void Clear()
    {
        foreach (var s in Sessions.Where(s => !s.IsRunning).ToList()) Sessions.Remove(s);
        Selected = Sessions.FirstOrDefault();
    }

    [RelayCommand] private void CancelSelected() => Selected?.Cancel();

    public void Navigate(AutomationRow row)
    {
        if (row.File is not null) NavigateRequested?.Invoke(this, row);
    }

    /// <summary>The selected session's text (Copy).</summary>
    public string SelectedText() => Selected?.Text() ?? string.Empty;
}
