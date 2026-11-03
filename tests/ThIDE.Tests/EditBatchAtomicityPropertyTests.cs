// The in-app batch edits (Link survey, Renumber, Normalize, Fix all, Format all, map sync, rename) all
// write through WorkspaceEditService, which promises all-or-nothing. These cases generate batches of
// creates and copies (often into new folders several levels deep) and edits to closed files and to
// documents open in the editor — clean or with unsaved changes — over invented files in mixed encodings
// and line endings, then make one write fail part-way: a file stands where a folder is needed, the
// target file or folder is read-only, or the new file's text cannot be encoded. A failed batch must leave
// the disk (bytes, modes, folders, no temporary files) and every open buffer as they were, and report
// nothing as written. A batch without a fault must land exactly: closed files in their own encoding,
// clean open files on disk and in their buffer, dirty buffers edited in place with the disk untouched.

using System.Collections.ObjectModel;
using System.Text;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class EditBatchAtomicityPropertyTests
{
    private const int Seed = 61031;
    private const int Cases = 80;

    private enum Fault { None, FileWhereAFolderIsNeeded, ReadOnlyFile, ReadOnlyFolder, Unencodable }

    private enum Open { Closed, Clean, Dirty }

    private sealed record Existing(string Path, byte[] Bytes, string Token, Open Open);

    private static bool CanMakeReadOnly { get; } = ProbeReadOnly();

    private static bool ProbeReadOnly()
    {
        if (OperatingSystem.IsWindows()) return false;
        var dir = Directory.CreateTempSubdirectory("thide-ro-probe-").FullName;
        try
        {
            var file = Path.Combine(dir, "f");
            File.WriteAllText(file, "x");
            File.SetUnixFileMode(file, UnixFileMode.UserRead);
            try { using var _ = new FileStream(file, FileMode.Open, FileAccess.Write); return false; }
            catch (UnauthorizedAccessException) { return true; }
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(dir)) File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_batch_that_fails_while_writing_leaves_disk_and_buffers_as_they_were()
    {
        var failures = new List<string>();
        int faulted = 0, clean = 0, withBuffers = 0;
        for (int i = 0; i < Cases; i++)
        {
            var rng = new Random(Seed + i);
            var fault = (Fault)rng.Next(5);
            if (!CanMakeReadOnly && fault is Fault.ReadOnlyFile or Fault.ReadOnlyFolder) fault = Fault.FileWhereAFolderIsNeeded;
            var (message, buffers) = await RunCase(rng, fault);
            if (message is not null) failures.Add($"case {i} (seed {Seed + i}, {fault}): {message}");
            if (fault == Fault.None) clean++; else faulted++;
            if (buffers) withBuffers++;
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
        Assert.True(faulted > 40 && clean > 8 && withBuffers > 20,
            $"the generator produced {faulted} faulted and {clean} clean batches, {withBuffers} with open documents");
    }

    private static async Task<(string? Message, bool Buffers)> RunCase(Random rng, Fault fault)
    {
        var root = Directory.CreateTempSubdirectory("thide-edit-atomic-").FullName;
        try
        {
            var existing = Populate(root, rng);
            var docs = new ObservableCollection<FileDocumentViewModel>();
            var requests = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in existing.Where(e => e.Open != Open.Closed))
            {
                var doc = new FileDocumentViewModel(e.Path, SourceFileIo.Read(e.Path).Text, new MeasurementsViewModel());
                if (e.Open == Open.Dirty) doc.DocumentText = "# unsaved note\n" + doc.DocumentText;
                doc.ApplyEditsRequested += (_, _) => requests[e.Path] = requests.GetValueOrDefault(e.Path) + 1;
                docs.Add(doc);
            }
            var service = new WorkspaceEditService(
                WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name == "get_Documents" ? docs : null),
                WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "SetActiveThconfigAsync" ? Task.FromResult(true) : null));

            var (batch, expectedDisk, expectedNew) = Plan(root, rng, existing, docs);
            int k = -1;
            if (fault != Fault.None) batch = WithFault(root, rng, batch, fault, out k);

            var before = Tree(root);
            var buffersBefore = docs.Select(d => (d.DocumentText, d.IsDirty)).ToList();

            var outcome = await service.ApplyAsync(batch);
            var after = Tree(root);
            var buffersAfter = docs.Select(d => (d.DocumentText, d.IsDirty)).ToList();

            if (fault != Fault.None)
            {
                if (outcome.Success) return ($"the faulty batch (fault at write {k + 1}) was reported as applied", docs.Count > 0);
                if (outcome.Problems.Count > 0) return ($"refused up front ({outcome.Describe()}), so the fault was never reached", docs.Count > 0);
                if (outcome.Written.Count > 0) return ($"reports {outcome.Written.Count} file(s) as written", docs.Count > 0);
                if (after != before) return ($"the disk changed although write {k + 1} failed:\n{Diff(before, after)}", docs.Count > 0);
                if (!buffersAfter.SequenceEqual(buffersBefore)) return ("an open buffer changed although the batch failed", docs.Count > 0);
                if (requests.Count > 0) return ("an editor was asked to apply edits although the batch failed", docs.Count > 0);
                return (null, docs.Count > 0);
            }

            if (!outcome.Success) return ($"a clean batch failed: {outcome.Describe()}", docs.Count > 0);
            foreach (var (path, bytes) in expectedDisk)
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    return ($"{Path.GetRelativePath(root, path)} holds different bytes from the batch's", docs.Count > 0);
            foreach (var (path, bytes) in expectedNew)
                if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    return ($"{Path.GetRelativePath(root, path)} was not created as planned", docs.Count > 0);
            foreach (var e in existing)
            {
                var doc = docs.FirstOrDefault(d => d.FilePath == e.Path);
                bool edited = batch.Edits.Any(g => g.FilePath == e.Path);
                if (e.Open == Open.Dirty)
                {
                    if (!File.ReadAllBytes(e.Path).AsSpan().SequenceEqual(e.Bytes)) return ($"the disk copy of dirty {Path.GetFileName(e.Path)} was written", true);
                    if (edited && (requests.GetValueOrDefault(e.Path) != 1 || !doc!.DocumentText.Contains("edited") || !doc.IsDirty))
                        return ($"dirty {Path.GetFileName(e.Path)} did not get the edits as one step in its buffer", true);
                }
                else if (e.Open == Open.Clean && edited)
                {
                    if (doc!.IsDirty || doc.DocumentText != SourceFileIo.Read(e.Path).Text)
                        return ($"clean {Path.GetFileName(e.Path)} and its buffer disagree after the batch", true);
                }
            }
            if (Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any()) return ("a temporary file was left behind", docs.Count > 0);
            return (null, docs.Count > 0);
        }
        finally
        {
            MakeWritable(root);
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- generator -----------------------------------------------------------------------------------

    private static readonly string[] Folders = ["caves", "caves/north", "caves/north/deep", "data"];

    private static List<Existing> Populate(string root, Random rng)
    {
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(root, f));
        Directory.CreateDirectory(Path.Combine(root, "locked"));
        File.WriteAllText(Path.Combine(root, "caves", "blocker.th"), "survey blocker\nendsurvey\n");
        File.WriteAllText(Path.Combine(root, "caves", "readonly.th"), "survey readonly\nendsurvey\n");

        var list = new List<Existing>();
        int n = rng.Next(1, 7);
        for (int i = 0; i < n; i++)
        {
            var token = $"token{i}x";
            var path = Path.Combine(root, Folders[rng.Next(Folders.Length)], $"f{i}.th");
            var eol = rng.Next(2) == 0 ? "\n" : "\r\n";
            byte[] bytes = rng.Next(4) switch
            {
                // Latin-2 behind an encoding line: ł is 0xB3, ś is 0xB6.
                0 => [.. Encoding.ASCII.GetBytes($"encoding iso8859-2{eol}survey s{i} -title \""), 0xB3, 0xB6,
                      .. Encoding.ASCII.GetBytes($"\"{eol}  # {token}{eol}endsurvey{eol}")],
                1 => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes($"survey s{i} -title \"Peștera\"{eol}  # {token}{eol}endsurvey{eol}")],
                _ => Encoding.UTF8.GetBytes($"survey s{i} -title \"Grotte\"{eol}  # {token}{eol}endsurvey{eol}"),
            };
            File.WriteAllBytes(path, bytes);
            list.Add(new Existing(path, bytes, token, (Open)rng.Next(3)));
        }
        return list;
    }

    private static (EditBatch Batch, Dictionary<string, byte[]> Disk, Dictionary<string, byte[]> New) Plan(
        string root, Random rng, List<Existing> existing, IList<FileDocumentViewModel> docs)
    {
        var edits = new List<FileTextEdits>();
        var creates = new List<FileCreate>();
        var copies = new List<FileCopy>();
        var disk = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var fresh = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var e in existing.Where(_ => rng.Next(3) > 0))
        {
            // Planned against what the editor shows when the file is open, else against the disk.
            var text = docs.FirstOrDefault(d => d.FilePath == e.Path)?.DocumentText ?? SourceFileIo.Read(e.Path).Text;
            int at = text.IndexOf(e.Token, StringComparison.Ordinal);
            edits.Add(new FileTextEdits(e.Path, [new TextEdit(at, e.Token.Length, e.Token, "edited")]));
            if (e.Open != Open.Dirty) disk[e.Path] = Replace(e.Bytes, e.Token, "edited");
        }
        int count = rng.Next(0, 4);
        for (int c = 0; c < count; c++)
        {
            var target = Path.Combine(root, Folders[rng.Next(Folders.Length)], $"new{c}", rng.Next(2) == 0 ? "" : "inner", $"n{c}.th");
            if (rng.Next(2) == 0 || existing.Count == 0)
            {
                var content = $"survey n{c}\nendsurvey\n";
                creates.Add(new FileCreate(target, content));
                fresh[target] = Encoding.UTF8.GetBytes(content);
            }
            else
            {
                var source = existing[rng.Next(existing.Count)];
                copies.Add(new FileCopy(source.Path, target));
                fresh[target] = source.Bytes;   // copies run before edits, so they take the original bytes
            }
        }
        if (edits.Count == 0 && creates.Count == 0 && copies.Count == 0)
        {
            var target = Path.Combine(root, "data", "only.th");
            creates.Add(new FileCreate(target, "survey only\nendsurvey\n"));
            fresh[target] = Encoding.UTF8.GetBytes("survey only\nendsurvey\n");
        }
        return (new EditBatch(edits, creates, copies), disk, fresh);
    }

    /// <summary>
    /// Inserts one change that fails while it is written. Writes go creates, then copies, then edits; the
    /// position is chosen among them so earlier writes have to be undone.
    /// </summary>
    private static EditBatch WithFault(string root, Random rng, EditBatch batch, Fault fault, out int k)
    {
        var deep = rng.Next(2) == 0 ? "" : Path.Combine("made", "deeper");
        var edits = batch.Edits.ToList();
        var creates = batch.Creates.ToList();
        var copies = batch.Copies.ToList();
        switch (fault)
        {
            case Fault.FileWhereAFolderIsNeeded:
            {
                var target = Path.Combine(root, "caves", "blocker.th", deep, "x.th");
                if (rng.Next(2) == 0) { k = rng.Next(creates.Count + 1); creates.Insert(k, new FileCreate(target, "survey x\nendsurvey\n")); }
                else { int at = rng.Next(copies.Count + 1); copies.Insert(at, new FileCopy(Path.Combine(root, "caves", "readonly.th"), target)); k = creates.Count + at; }
                break;
            }
            case Fault.ReadOnlyFolder:
            {
                var locked = Path.Combine(root, "locked");
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                var target = Path.Combine(locked, deep, "x.th");
                if (rng.Next(2) == 0) { k = rng.Next(creates.Count + 1); creates.Insert(k, new FileCreate(target, "survey x\nendsurvey\n")); }
                else { int at = rng.Next(copies.Count + 1); copies.Insert(at, new FileCopy(Path.Combine(root, "caves", "readonly.th"), target)); k = creates.Count + at; }
                break;
            }
            case Fault.ReadOnlyFile:
            {
                var target = Path.Combine(root, "caves", "readonly.th");
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.GroupRead);
                int at = rng.Next(edits.Count + 1);
                edits.Insert(at, new FileTextEdits(target, [new TextEdit(7, 8, "readonly", "changed!")]));
                k = creates.Count + copies.Count + at;
                break;
            }
            default:
            {
                // Latin-2 has no euro sign: the new file's own encoding line cannot hold its text.
                k = rng.Next(creates.Count + 1);
                creates.Insert(k, new FileCreate(Path.Combine(root, "data", deep, "euro.th"), "encoding iso8859-2\nsurvey e -title \"€\"\nendsurvey\n"));
                break;
            }
        }
        return new EditBatch(edits, creates, copies);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static byte[] Replace(byte[] bytes, string token, string with)
    {
        var t = Encoding.ASCII.GetBytes(token);
        int at = bytes.AsSpan().IndexOf(t);
        return [.. bytes[..at], .. Encoding.ASCII.GetBytes(with), .. bytes[(at + t.Length)..]];
    }

    private static string Tree(string root)
    {
        var sb = new StringBuilder();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0 }).OrderBy(e => e, StringComparer.Ordinal))
        {
            sb.Append(Path.GetRelativePath(root, entry));
            if (!OperatingSystem.IsWindows()) sb.Append(' ').Append((int)File.GetUnixFileMode(entry));
            sb.Append(Directory.Exists(entry) ? " dir" : " " + SourceFileIo.Digest(File.ReadAllBytes(entry))).Append('\n');
        }
        return sb.ToString();
    }

    private static string Diff(string before, string after)
    {
        var a = before.Split('\n').ToHashSet();
        var b = after.Split('\n').ToHashSet();
        return string.Join("\n", a.Except(b).Select(l => "- " + l).Concat(b.Except(a).Select(l => "+ " + l)));
    }

    private static void MakeWritable(string root)
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Prepend(root).ToList())
            try { File.SetUnixFileMode(entry, File.GetUnixFileMode(entry) | UnixFileMode.UserWrite | UnixFileMode.UserRead | (Directory.Exists(entry) ? UnixFileMode.UserExecute : 0)); }
            catch (IOException) { }
    }
}
