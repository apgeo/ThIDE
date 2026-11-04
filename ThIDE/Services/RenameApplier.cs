using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Therion.Workspace.Editing;
using ThIDE.Resources;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Services;

/// <summary>Writes a symbol rename to every file it reaches, as one workspace undo step.</summary>
internal static class RenameApplier
{
    /// <summary>
    /// Replaces each file's hits with <paramref name="newName"/>, writes the files and updates open
    /// buffers, then records the whole rename in <paramref name="history"/>. Returns the record, or
    /// null when nothing was written. A file that cannot be written throws
    /// <see cref="RenameWriteException"/> after the files already written are put back.
    /// </summary>
    public static async Task<WorkspaceUndoRecord?> ApplyAsync(
        List<RenameFileChanges> changes,
        string oldName,
        string newName,
        IDocumentService docs,
        IWorkspaceSession? session,
        INotificationService? notifications,
        IWorkspaceUndoService? history)
    {
        // The rename is an app-initiated write, so suppress the file-watcher for each file (like a
        // normal Save) and update any open buffer in place — instead of a "reloaded from disk"
        // banner/toast, which was misleading for a change the app made itself (#1).

        // Encode every file before writing any: one file whose encoding cannot hold the new name stops
        // the whole rename, instead of leaving references to a survey that was not renamed.
        var planned = new List<(RenameFileChanges Changes, string Text, byte[] Bytes, TextFileFormat Format, FileDocumentViewModel? Open)>();
        foreach (var fc in changes)
        {
            var sorted = new List<(int, int)>(fc.Hits);
            sorted.Sort((a, b) => b.Item1.CompareTo(a.Item1));

            var sb = new System.Text.StringBuilder(fc.FileText);
            foreach (var (start, length) in sorted)
                sb.Remove(start, length).Insert(start, newName);

            var newText = sb.ToString();
            var open = docs.Documents.FirstOrDefault(d => FilePaths.Comparer.Equals(d.FilePath, fc.FilePath));
            try
            {
                var (bytes, format) = await Task.Run(() => SourceFileIo.EncodeForSave(fc.FilePath, newText, open?.Format));
                planned.Add((fc, newText, bytes, format, open));
            }
            catch (UnrepresentableCharacterException ex)
            {
                notifications?.Error(Tr.Get("Notif_SaveEncodingTitle"),
                    string.Format(Tr.Get(ex is UndecodableBytesException ? "Notif_SaveUndecodable" : "Notif_SaveEncoding"),
                        System.IO.Path.GetFileName(fc.FilePath), ex.Character, ex.EncodingName));
                return null;
            }
            catch { /* unreadable: left out of the rename */ }
        }

        // Write every file before touching a buffer. A file that cannot be written (read-only, the disk
        // full, held by another program) puts back the ones already written and stops the rename with
        // an error the user sees: half a rename leaves references to a name that no longer exists.
        var done = new List<(RenameFileChanges Changes, string Text, TextFileFormat Format, FileDocumentViewModel? Open, byte[] Original)>();
        foreach (var (fc, newText, bytes, format, open) in planned)
        {
            session?.SuppressSelfWrite(fc.FilePath);
            try
            {
                var original = await Task.Run(() => System.IO.File.ReadAllBytes(fc.FilePath));
                await Task.Run(() => SourceFileIo.WriteOver(fc.FilePath, bytes));
                done.Add((fc, newText, format, open, original));
            }
            catch (Exception ex)
            {
                foreach (var w in done)
                {
                    session?.SuppressSelfWrite(w.Changes.FilePath);
                    try { await Task.Run(() => SourceFileIo.WriteOver(w.Changes.FilePath, w.Original)); }
                    catch { /* reporting the failed write matters more */ }
                }
                throw new RenameWriteException(
                    string.Format(Tr.Get("Rename_WriteFailed"), System.IO.Path.GetFileName(fc.FilePath), IoErrorText.Reason(ex)), ex);
            }
        }

        var written = new List<FileChange>();
        foreach (var (fc, newText, format, open, _) in done)
        {
            if (open is not null) open.Format = format;
            written.Add(new FileChange(fc.FilePath, fc.FileText, newText));

            foreach (var doc in docs.Documents)
            {
                if (!FilePaths.Comparer.Equals(doc.FilePath, fc.FilePath)) continue;

                // The active file's new text already incorporates its unsaved edits, so update it
                // silently. Any OTHER tab with unsaved edits keeps the conflict banner so those
                // edits aren't discarded; a clean tab just reflects the rename silently.
                if (!ReferenceEquals(doc, docs.Active) && doc.IsDirty)
                    doc.NotifyExternalChange(DateTime.Now, deleted: false, autoReload: true);
                else
                    doc.SetText(newText, reparse: true);
                break;
            }
        }

        if (written.Count == 0) return null;
        // One step for every file: Edit ▸ Undo Rename (and the notification's Undo) puts them all back.
        var record = new WorkspaceUndoRecord(Tr.Get("WsUndo_OpRename"),
            string.Format(Tr.Get("Rename_Done"), oldName, newName, written.Count), written);
        history?.Push(record);
        return record;
    }
}

/// <summary>A rename stopped because a file could not be written; the message is in the UI language.</summary>
public sealed class RenameWriteException(string message, Exception inner) : System.IO.IOException(message, inner);
