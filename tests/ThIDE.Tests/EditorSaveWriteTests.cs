using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Workspace;
using ThIDE.Services;

namespace ThIDE.Tests;

// The editor's save replaces the file through a temporary sibling, so a failed write cannot leave the
// survey truncated; a link, the file mode and a read-only file are treated as before.
public class EditorSaveWriteTests
{
    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<(NotificationKind Kind, string Message)> Items { get; } = [];
        public event EventHandler<AppNotification>? Posted { add { } remove { } }
        public event EventHandler? UnreadChanged { add { } remove { } }
        public ObservableCollection<AppNotification> History { get; } = [];
        public int UnreadCount => 0;
        public void Post(AppNotification n) => Items.Add((n.Kind, n.Message));
        public void Info(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Info, a, ac));
        public void Success(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Success, a, ac));
        public void Warning(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Warning, a, ac));
        public void Error(string t, string m, string? a = null, Action? ac = null) => Post(new AppNotification(t, m, NotificationKind.Error, a, ac));
        public void MarkAllRead() { }
        public void Clear() { }
    }

    private static DocumentService NewService(INotificationService? notifications = null) =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()), notifications: notifications);

    [Fact]
    public async Task Save_replaces_the_file_instead_of_truncating_it()
    {
        if (OperatingSystem.IsWindows()) return; // replacing a file that is held open needs POSIX rename semantics
        using var dir = new TempDir();
        var path = dir.Write("a.th", "survey a\nendsurvey\n");
        var svc = NewService();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();
        doc.DocumentText = "survey a\n  # edited\nendsurvey\n";

        // A handle opened before the save still reads the old file when the save swapped in a new one;
        // an in-place write (truncate, then write) would show it the new text.
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.True(await svc.SaveDocumentAsync(doc));

        using var reader = new StreamReader(held, Encoding.UTF8);
        Assert.Equal("survey a\nendsurvey\n", reader.ReadToEnd());
        Assert.Equal("survey a\n  # edited\nendsurvey\n", File.ReadAllText(path));
        Assert.False(doc.IsDirty);
        Assert.Single(Directory.GetFiles(dir.Path));   // no temporary file left behind
    }

    [Fact]
    public async Task Save_through_a_symlink_writes_the_linked_file_and_keeps_its_mode()
    {
        if (OperatingSystem.IsWindows()) return; // symlinks need a privilege there
        using var dir = new TempDir();
        var real = dir.Write("shared/fixes.th", "survey fixes\nendsurvey\n");
        File.SetUnixFileMode(real, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead);
        var link = Path.Combine(dir.Path, "fixes.th");
        File.CreateSymbolicLink(link, real);
        var svc = NewService();
        await svc.OpenFileAsync(link);
        var doc = svc.Documents.Single();
        doc.DocumentText = "survey fixes\n  # edited\nendsurvey\n";

        Assert.True(await svc.SaveDocumentAsync(doc));

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("survey fixes\n  # edited\nendsurvey\n", File.ReadAllText(real));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead,
            File.GetUnixFileMode(real));
    }

    [Fact]
    public async Task Save_of_a_read_only_file_still_fails_and_keeps_the_edits()
    {
        if (OperatingSystem.IsWindows()) return;
        using var dir = new TempDir();
        var path = dir.Write("ro.th", "survey ro\nendsurvey\n");
        var svc = NewService();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();
        doc.DocumentText = "survey ro\n  # edited\nendsurvey\n";
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        try
        {
            try { using (new FileStream(path, FileMode.Open, FileAccess.Write)) { } return; } // root ignores the mode
            catch (UnauthorizedAccessException) { }

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SaveDocumentAsync(doc));
            Assert.Equal("survey ro\nendsurvey\n", File.ReadAllText(path));
            Assert.True(doc.IsDirty);
        }
        finally { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public async Task A_failed_write_is_notified_and_the_document_stays_unsaved()
    {
        using var dir = new TempDir();
        var folder = Path.Combine(dir.Path, "gone");
        var path = dir.Write("gone/a.th", "survey a\nendsurvey\n");
        var notifications = new FakeNotifications();
        var svc = NewService(notifications);
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();
        doc.DocumentText = "survey a\n  # edited\nendsurvey\n";
        Directory.Delete(folder, recursive: true);   // nowhere to write to

        await Assert.ThrowsAnyAsync<IOException>(() => svc.SaveDocumentAsync(doc));

        Assert.True(doc.IsDirty);
        Assert.Contains(notifications.Items, n => n.Kind == NotificationKind.Error && n.Message.Contains("a.th"));
    }
}
