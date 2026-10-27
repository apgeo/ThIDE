using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Therion.Workspace.Editing;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Services;

/// <param name="Problems">Why the batch was refused before anything was written (empty on success).</param>
/// <param name="Error">An I/O failure part-way through, after some files may have been written.</param>
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
/// in step); every other file is rewritten on disk in its own encoding. Must be called on the UI
/// thread: it reads and changes open documents.
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

        var check = EditBatchChecker.Check(batch, CurrentText, p => File.Exists(p) || Directory.Exists(p));
        if (!check.Ok) return new EditOutcome(check.Problems, null, []);

        var written = new List<string>();
        try
        {
            foreach (var create in batch.Creates)
            {
                session.SuppressSelfWrite(create.FilePath);
                SourceFileIo.Create(create.FilePath, create.Content);
                written.Add(create.FilePath);
            }
            foreach (var copy in batch.Copies)
            {
                if (Path.GetDirectoryName(copy.DestinationPath) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
                session.SuppressSelfWrite(copy.DestinationPath);
                File.Copy(copy.SourcePath, copy.DestinationPath, overwrite: false);
                written.Add(copy.DestinationPath);
            }
            foreach (var group in batch.Edits)
            {
                var newText = check.NewTexts[group.FilePath];
                if (open.TryGetValue(Key(group.FilePath), out var doc))
                {
                    bool wasClean = !doc.IsDirty;
                    if (!doc.ApplyEdits(group.Edits))
                        return new EditOutcome([new EditProblem(group.FilePath, EditProblemKind.Stale)], null, written);
                    if (wasClean)
                    {
                        session.SuppressSelfWrite(doc.FilePath);
                        SourceFileIo.Write(SourceFileIo.Read(doc.FilePath), newText);
                        doc.SetText(newText, reparse: true);
                    }
                }
                else
                {
                    session.SuppressSelfWrite(group.FilePath);
                    SourceFileIo.Write(onDisk[group.FilePath], newText);
                }
                written.Add(group.FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnrepresentableCharacterException)
        {
            return new EditOutcome([], ex.Message, written);
        }

        if (batch.RequiresWorkspaceReload && session.ActiveThconfig is { } active)
            await session.SetActiveThconfigAsync(active.FullPath, ct).ConfigureAwait(true);

        return new EditOutcome([], null, written);

        string? CurrentText(string path)
        {
            if (open.TryGetValue(Key(path), out var d)) return d.DocumentText;
            if (!File.Exists(path)) return null;
            var file = SourceFileIo.Read(path);
            onDisk[path] = file;
            return file.Text;
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
        var full = Path.GetFullPath(path);
        return OperatingSystem.IsLinux() ? full : full.ToUpperInvariant();
    }
}
