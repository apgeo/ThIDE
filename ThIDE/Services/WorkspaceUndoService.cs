// Workspace-level undo for the operations that write several files at once (rename, Replace in
// Files, New trip, Link survey, Renumber, Format all, Normalise, Fix all). Each operation pushes one
// record holding every file's text before and after; Undo puts all of them back in one step, on disk
// and in any open editor, and Redo applies them again. A file's editor undo cannot do this: the other
// files of the operation are often not open, and a file written to disk has no editor history.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Therion.Workspace.Editing;
using ThIDE.Resources;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Services;

/// <summary>One file in a <see cref="WorkspaceUndoRecord"/>.</summary>
/// <param name="Before">The text before the operation; null when the operation created the file.</param>
/// <param name="After">The text the operation left. For a created file <see cref="Created"/> is compared instead.</param>
public sealed record FileChange(string Path, string? Before, string After)
{
    /// <summary>For a file the operation created: the bytes it wrote. Undo deletes the file only while it still holds them.</summary>
    public byte[]? Created { get; init; }

    /// <summary>The change went into an editor buffer with unsaved edits, not to disk; undo restores that buffer only.</summary>
    public bool BufferOnly { get; init; }
}

/// <summary>One multi-file operation, reversible as a whole.</summary>
/// <param name="Label">The operation's name for "Undo {Label}" (in the UI language).</param>
/// <param name="Summary">What it did, for the notification that offers the undo.</param>
public sealed record WorkspaceUndoRecord(string Label, string Summary, IReadOnlyList<FileChange> Files)
{
    /// <summary>An operation's name from its menu text ("Format all files…" → "Format all files").</summary>
    public static string OperationName(string menuText) => menuText.Replace("_", "").TrimEnd('…', '.', ' ');

    /// <summary>The changed files' names, for the notification and the outcome messages.</summary>
    public string FileNames(int max = 5)
    {
        var names = Files.Select(f => System.IO.Path.GetFileName(f.Path)).Distinct(StringComparer.Ordinal).ToList();
        var shown = string.Join(", ", names.Take(max));
        return names.Count > max ? shown + string.Format(Tr.Get("WsUndo_More"), names.Count - max) : shown;
    }
}

public enum WorkspaceUndoStatus
{
    /// <summary>Every file was put back.</summary>
    Done,
    /// <summary>No such record to undo (or redo): nothing was changed.</summary>
    Nothing,
    /// <summary>A file changed since the operation; nothing was changed.</summary>
    Refused,
    /// <summary>A file could not be written; what was written has been rolled back.</summary>
    Failed,
}

/// <param name="Message">What happened, in the UI language (null for <see cref="WorkspaceUndoStatus.Nothing"/>).</param>
public sealed record WorkspaceUndoOutcome(WorkspaceUndoStatus Status, WorkspaceUndoRecord? Record, int Files, string? Message);

/// <summary>
/// The workspace undo history. Must be used on the UI thread: undo and redo read and change open
/// documents. Undo refuses, changing nothing, when any file of the record no longer holds the text the
/// operation left (open buffer, else disk), so a later edit is never overwritten.
/// </summary>
public interface IWorkspaceUndoService
{
    /// <summary>Raised when the history changes (a push, an undo, a redo).</summary>
    event EventHandler? Changed;

    /// <summary>Raised for every record pushed (the app offers its undo in a notification).</summary>
    event EventHandler<WorkspaceUndoRecord>? Pushed;

    /// <summary>The record Undo would revert, or null.</summary>
    WorkspaceUndoRecord? NextUndo { get; }

    /// <summary>The record Redo would apply again, or null.</summary>
    WorkspaceUndoRecord? NextRedo { get; }

    /// <summary>Records an operation that has been applied. A record with no files is ignored. Clears the redo history.</summary>
    void Push(WorkspaceUndoRecord record);

    /// <summary>Reverts <paramref name="record"/> (default: the latest), when it has not been undone yet.</summary>
    Task<WorkspaceUndoOutcome> UndoAsync(WorkspaceUndoRecord? record = null);

    /// <summary>Applies the latest undone record again.</summary>
    Task<WorkspaceUndoOutcome> RedoAsync();
}

public sealed class WorkspaceUndoService(IDocumentService documents, IWorkspaceSession? session = null) : IWorkspaceUndoService
{
    // Whole-file texts are kept per record, so the history is short.
    private const int MaxRecords = 20;

    private readonly List<WorkspaceUndoRecord> _undo = [];
    private readonly List<WorkspaceUndoRecord> _redo = [];

    public event EventHandler? Changed;
    public event EventHandler<WorkspaceUndoRecord>? Pushed;

    public WorkspaceUndoRecord? NextUndo => _undo.Count > 0 ? _undo[^1] : null;
    public WorkspaceUndoRecord? NextRedo => _redo.Count > 0 ? _redo[^1] : null;

    public void Push(WorkspaceUndoRecord record)
    {
        if (record.Files.Count == 0) return;
        _undo.Add(record);
        if (_undo.Count > MaxRecords) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
        Pushed?.Invoke(this, record);
    }

    public Task<WorkspaceUndoOutcome> UndoAsync(WorkspaceUndoRecord? record = null) =>
        RunAsync(record ?? NextUndo, _undo, _redo, undo: true);

    public Task<WorkspaceUndoOutcome> RedoAsync() => RunAsync(NextRedo, _redo, _undo, undo: false);

    // One planned write: the text (or bytes) to put back, and the open document it goes into.
    private sealed record Step(FileChange Change, string? Text, byte[]? Bytes, TextFileFormat? Format, FileDocumentViewModel? Doc);

    private async Task<WorkspaceUndoOutcome> RunAsync(WorkspaceUndoRecord? record, List<WorkspaceUndoRecord> from,
        List<WorkspaceUndoRecord> to, bool undo)
    {
        if (record is null || !from.Contains(record)) return new(WorkspaceUndoStatus.Nothing, record, 0, null);

        // Check every file and encode every write before writing any.
        var steps = new List<Step>();
        foreach (var change in record.Files)
        {
            var doc = documents.Documents.FirstOrDefault(d => FilePaths.Same(d.FilePath, change.Path));
            if (!Holds(change, doc, afterState: undo))
                return new(WorkspaceUndoStatus.Refused, record, 0,
                    string.Format(Tr.Get("WsUndo_Refused"), Path.GetFileName(change.Path)));

            var target = undo ? change.Before : change.After;
            if (change.Created is { } created)
            {
                steps.Add(undo ? new Step(change, null, null, null, doc) : new Step(change, change.After, created, null, doc));
                continue;
            }
            if (change.BufferOnly && doc is not null) { steps.Add(new Step(change, target, null, null, doc)); continue; }
            try
            {
                var (bytes, format) = SourceFileIo.EncodeForSave(change.Path, target!, doc?.Format);
                steps.Add(new Step(change, target, bytes, format, doc));
            }
            catch (UnrepresentableCharacterException ex)
            {
                return new(WorkspaceUndoStatus.Failed, record, 0, string.Format(Tr.Get("WsUndo_Encoding"),
                    Path.GetFileName(change.Path), ex.Character, ex.EncodingName));
            }
        }

        // Disk first, each write with a rollback; buffers last (they cannot fail).
        var rollback = new Stack<Action>();
        try
        {
            foreach (var step in steps)
            {
                if (step.Bytes is null && step.Text is not null) continue;   // buffer only
                var path = step.Change.Path;
                var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
                session?.SuppressSelfWrite(path);
                if (step.Bytes is null) File.Delete(path);
                else SourceFileIo.WriteOver(path, step.Bytes);
                rollback.Push(() =>
                {
                    session?.SuppressSelfWrite(path);
                    if (original is null) File.Delete(path);
                    else SourceFileIo.WriteOver(path, original);
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            while (rollback.Count > 0)
            {
                try { rollback.Pop()(); } catch (Exception) { /* best effort: report the original failure */ }
            }
            return new(WorkspaceUndoStatus.Failed, record, 0, ex.Message);
        }

        bool closedFileChanged = false;
        foreach (var step in steps)
        {
            if (step.Doc is null) { closedFileChanged = true; continue; }
            if (step.Bytes is null && step.Text is null)
            {
                // The file is gone: close its (clean) tab, through the dock when it shows one.
                if (step.Doc.Factory is not null) step.Doc.CloseTabCommand.Execute(null);
                else documents.CloseDocument(step.Doc);
                closedFileChanged = true;
                continue;
            }
            if (step.Bytes is null) { step.Doc.DocumentText = step.Text!; continue; }   // unsaved buffer: stays unsaved
            if (step.Format is { } format) step.Doc.Format = format;
            step.Doc.SetText(step.Text!, reparse: true);
        }

        from.Remove(record);
        to.Add(record);
        Changed?.Invoke(this, EventArgs.Empty);

        // Files no editor holds changed (or appeared or went) on disk: the workspace model must follow.
        if (closedFileChanged && session?.ActiveThconfig is { } active)
            await session.SetActiveThconfigAsync(active.FullPath).ConfigureAwait(true);

        return new(WorkspaceUndoStatus.Done, record, record.Files.Count,
            Tr.Plural(undo ? "WsUndo_Done" : "WsUndo_Redone", record.Files.Count, record.Label, record.FileNames()));
    }

    // Whether the file is in the state the step starts from: the state the operation left (undo) or
    // the one it found (redo). An open document answers with its buffer, a closed one from disk.
    private static bool Holds(FileChange change, FileDocumentViewModel? doc, bool afterState)
    {
        if (change.Created is { } created)
        {
            if (!afterState) return doc is null && !File.Exists(change.Path);
            if (doc is { IsDirty: true }) return false;
            try { return File.Exists(change.Path) && File.ReadAllBytes(change.Path).AsSpan().SequenceEqual(created); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }

        var expected = afterState ? change.After : change.Before;
        if (doc is not null) return string.Equals(doc.DocumentText, expected, StringComparison.Ordinal);
        try { return File.Exists(change.Path) && string.Equals(SourceFileIo.Read(change.Path).Text, expected, StringComparison.Ordinal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
