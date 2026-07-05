// Notifications / toast center.
//
// A small app-wide hub for transient background events (build done, file changed on disk,
// tool not found, import/export failures). Components post an AppNotification; the View
// shows it as a corner toast (WindowNotificationManager) and the toolbar bell flyout keeps
// a capped, navigable HISTORY with an optional action per entry.
//
// Separate from ILogService (the verbose activity-log panel): this layer is purely the
// user-facing "toast + bell" surface and intentionally only carries curated, actionable
// events, not every log line.

using System;
using System.Collections.ObjectModel;

namespace ThIDE.Services;

/// <summary>Visual category of a notification (drives toast color + bell glyph).</summary>
public enum NotificationKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// One user-facing notification. <see cref="Action"/> (when set) is invoked from the toast
/// click and the history "action" button; <see cref="ActionLabel"/> is its button caption.
/// </summary>
public sealed class AppNotification
{
    public AppNotification(string title, string message, NotificationKind kind,
        string? actionLabel = null, Action? action = null)
    {
        Time = DateTimeOffset.Now;
        Title = title ?? string.Empty;
        Message = message ?? string.Empty;
        Kind = kind;
        ActionLabel = actionLabel;
        Action = action;
    }

    public DateTimeOffset Time { get; }
    public string Title { get; }
    public string Message { get; }
    public NotificationKind Kind { get; }
    public string? ActionLabel { get; }
    public Action? Action { get; }

    /// <summary>True when this notification carries an invocable action (for binding visibility).</summary>
    public bool HasAction => Action is not null && !string.IsNullOrEmpty(ActionLabel);

    /// <summary>Short "HH:mm" time shown in the history list.</summary>
    public string TimeText => Time.ToLocalTime().ToString("HH:mm");

    private System.Windows.Input.ICommand? _actionCommand;
    /// <summary>Command wrapper around <see cref="Action"/> for the history "action" button.</summary>
    public System.Windows.Input.ICommand? ActionCommand =>
        Action is null ? null : _actionCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand(Action);
}

/// <summary>App-wide notification hub. Raises <see cref="Posted"/> for the toast layer and keeps a
/// capped <see cref="History"/> for the bell flyout. Implementations are UI-thread agnostic.</summary>
public interface INotificationService
{
    /// <summary>Raised for every posted notification (the View drives the toast from this).</summary>
    event EventHandler<AppNotification>? Posted;
    /// <summary>Raised when <see cref="UnreadCount"/> changes (so a badge can refresh).</summary>
    event EventHandler? UnreadChanged;

    /// <summary>Most-recent-first history, capped. Bind the bell flyout list to this.</summary>
    ObservableCollection<AppNotification> History { get; }
    /// <summary>Number of notifications posted since the bell flyout was last opened.</summary>
    int UnreadCount { get; }

    void Post(AppNotification notification);
    void Info(string title, string message, string? actionLabel = null, Action? action = null);
    void Success(string title, string message, string? actionLabel = null, Action? action = null);
    void Warning(string title, string message, string? actionLabel = null, Action? action = null);
    void Error(string title, string message, string? actionLabel = null, Action? action = null);

    /// <summary>Resets the unread badge (called when the bell flyout is opened).</summary>
    void MarkAllRead();
    /// <summary>Clears the history list.</summary>
    void Clear();
}

public sealed class NotificationService : INotificationService
{
    private const int MaxHistory = 200;

    public event EventHandler<AppNotification>? Posted;
    public event EventHandler? UnreadChanged;

    public ObservableCollection<AppNotification> History { get; } = new();
    public int UnreadCount { get; private set; }

    public void Post(AppNotification notification)
    {
        if (notification is null) return;
        Run(() =>
        {
            History.Insert(0, notification);
            while (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
            UnreadCount++;
            UnreadChanged?.Invoke(this, EventArgs.Empty);
            Posted?.Invoke(this, notification);
        });
    }

    public void Info(string title, string message, string? actionLabel = null, Action? action = null)
        => Post(new AppNotification(title, message, NotificationKind.Info, actionLabel, action));
    public void Success(string title, string message, string? actionLabel = null, Action? action = null)
        => Post(new AppNotification(title, message, NotificationKind.Success, actionLabel, action));
    public void Warning(string title, string message, string? actionLabel = null, Action? action = null)
        => Post(new AppNotification(title, message, NotificationKind.Warning, actionLabel, action));
    public void Error(string title, string message, string? actionLabel = null, Action? action = null)
        => Post(new AppNotification(title, message, NotificationKind.Error, actionLabel, action));

    public void MarkAllRead()
    {
        if (UnreadCount == 0) return;
        Run(() => { UnreadCount = 0; UnreadChanged?.Invoke(this, EventArgs.Empty); });
    }

    public void Clear() => Run(() =>
    {
        History.Clear();
        UnreadCount = 0;
        UnreadChanged?.Invoke(this, EventArgs.Empty);
    });

    // History is an ObservableCollection bound directly to the UI, so all mutations must run on
    // the UI thread (notifications are frequently posted from background build/parse threads).
    private static void Run(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }
}
