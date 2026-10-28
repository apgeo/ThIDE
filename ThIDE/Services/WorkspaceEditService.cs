using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Workspace.Editing;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Services;

/// <param name="Problems">Why the batch was refused; nothing was written (or everything was rolled back).</param>
/// <param name="Error">An I/O failure part-way through; what was written has been rolled back.</param>
/// <param name="Written">Every path created, copied or edited, in the order it happened.</param>
public sealed record EditOutcome(
    IReadOnlyList<EditProblem> Problems,
    string? Error,
    IReadOnlyList<string> Written)
{
    public bool Success => Problems.Count == 0 && Error is null;
}

/// <summary>
/// The one way in-app features write Therion files. Edits to a file open in the editor go into its
/// buffer as one undo step (and are saved when the buffer was clean, so the editor and the disk stay
/// in step); every other file is rewritten on disk in its own encoding. All-or-nothing: everything is
/// checked first, and a failure while writing rolls back what was already written. Must be called on
/// the UI thread: it reads and changes open documents.
/// </summary>
public interface IWorkspaceEditService
{
    Task<EditOutcome> ApplyAsync(EditBatch batch, CancellationToken ct = default);
}

public sealed class WorkspaceEditService(IDocumentService documents, IWorkspaceSession session) : IWorkspaceEditService
{
    public async Task<EditOutcome> ApplyAsync(EditBatch batch, CancellationToken ct = default)
    {
        var open = OpenDocumentsByPath(documents.Documents);
        var onDisk = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        var problems = new List<EditProblem>();

        EditBatchCheck check;
        try
        {
            check = EditBatchChecker.Check(batch, CurrentText, p => File.Exists(p) || Directory.Exists(p), File.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new EditOutcome([], ex.Message, []);
        }
        problems.AddRange(check.Problems);
        if (problems.Count == 0)
        {
            foreach (var group in batch.Edits)
            {
                var doc = open.GetValueOrDefault(Key(group.FilePath));
                if (doc is { IsDirty: true })
                {
                    // The edit can only go into the buffer; a reload would not see it.
                    if (batch.RequiresWorkspaceReload) problems.Add(new(group.FilePath, EditProblemKind.UnsavedChanges));
                    continue;
                }
                // Clean or closed: the file on disk is rewritten, in its own encoding.
                if (!onDisk.TryGetValue(group.FilePath, out var file))
                {
                    problems.Add(new(group.FilePath, EditProblemKind.Unreadable));
                    continue;
                }
                if (doc is not null && !string.Equals(doc.DocumentText, file.Text, StringComparison.Ordinal))
                    problems.Add(new(group.FilePath, EditProblemKind.OpenBufferDiffers));
                else if (!SourceFileIo.CanEncode(file, check.NewTexts[group.FilePath]))
                    problems.Add(new(group.FilePath, EditProblemKind.Unencodable));
            }
        }
        if (problems.Count > 0) return new EditOutcome(problems, null, []);

        // Disk first, with an undo record for each step; buffers last (they cannot fail now: the
        // checks above ran against the same text on the same thread).
        var written = new List<string>();
        var undo = new Stack<Action>();
        try
        {
            foreach (var create in batch.Creates)
            {
                session.SuppressSelfWrite(create.FilePath);
                SourceFileIo.Create(create.FilePath, create.Content);
                undo.Push(() => File.Delete(create.FilePath));
                written.Add(create.FilePath);
            }
            foreach (var copy in batch.Copies)
            {
                if (Path.GetDirectoryName(copy.DestinationPath) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
                session.SuppressSelfWrite(copy.DestinationPath);
                File.Copy(copy.SourcePath, copy.DestinationPath, overwrite: false);
                undo.Push(() => File.Delete(copy.DestinationPath));
                written.Add(copy.DestinationPath);
            }
            foreach (var group in batch.Edits)
            {
                if (open.GetValueOrDefault(Key(group.FilePath)) is { IsDirty: true }) continue;
                var file = onDisk[group.FilePath];
                var original = File.ReadAllBytes(file.Path);
                session.SuppressSelfWrite(file.Path);
                SourceFileIo.Write(file, check.NewTexts[group.FilePath]);
                undo.Push(() => { session.SuppressSelfWrite(file.Path); SourceFileIo.WriteOver(file.Path, original); });
                written.Add(group.FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnrepresentableCharacterException)
        {
            while (undo.Count > 0)
            {
                try { undo.Pop()(); } catch (Exception) { /* best effort: report the original failure */ }
            }
            return new EditOutcome([], ex.Message, []);
        }

        foreach (var group in batch.Edits)
        {
            if (!open.TryGetValue(Key(group.FilePath), out var doc)) continue;
            bool wasDirty = doc.IsDirty;
            doc.ApplyEdits(group.Edits);
            if (wasDirty) written.Add(group.FilePath);
            else doc.SetText(check.NewTexts[group.FilePath], reparse: false); // on disk already; ApplyEdits scheduled the reparse
        }

        if (batch.RequiresWorkspaceReload && session.ActiveThconfig is { } active)
            await session.SetActiveThconfigAsync(active.FullPath, ct).ConfigureAwait(true);

        return new EditOutcome([], null, written);

        // Open documents answer with their buffer; closed ones are read (and remembered) from disk.
        // A clean open document is read from disk too, so its disk text can be compared above.
        string? CurrentText(string path)
        {
            open.TryGetValue(Key(path), out var d);
            if (d is { IsDirty: true }) return d.DocumentText;
            if (!File.Exists(path)) return d?.DocumentText;
            var file = SourceFileIo.Read(path);
            onDisk[path] = file;
            return d?.DocumentText ?? file.Text;
        }
    }

    /// <summary>Open documents keyed by full path, so a planner's path and a tab's path compare equal.</summary>
    internal static Dictionary<string, FileDocumentViewModel> OpenDocumentsByPath(IEnumerable<FileDocumentViewModel> docs)
    {
        var map = new Dictionary<string, FileDocumentViewModel>(StringComparer.Ordinal);
        foreach (var d in docs.Where(d => !string.IsNullOrEmpty(d.FilePath) && Path.IsPathRooted(d.FilePath)))
            map.TryAdd(Key(d.FilePath), d);
        return map;
    }

    private static string Key(string path)
    {
        var full = FilePaths.Full(path);
        return OperatingSystem.IsLinux() ? full : full.ToUpperInvariant();
    }
}
