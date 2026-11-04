// A plan the agent's tools hand to the MutationEngine is all-or-nothing. These cases generate plans of
// edits, whole-file writes, creates, copies and new folders (often several folders deep) over invented
// files in mixed encodings and line endings, and make one change fail while it is being written:
// a file stands where a folder is needed, the target file or folder is read-only, or the new text
// cannot be encoded. Whatever came before it must be undone — the bytes, the modes and the folder tree
// are as they were, with no temporary file left behind. Plans without a fault must land exactly.

using System.Text;
using Therion.Mcp.Mutations;
using Therion.Workspace.Editing;

namespace Therion.Mcp.Tests;

public class MutationAtomicityPropertyTests
{
    private const int Seed = 4127;
    private const int Cases = 90;

    private enum Fault { None, FileWhereAFolderIsNeeded, ReadOnlyFile, ReadOnlyFolder, Unencodable }

    private sealed record Existing(string Relative, byte[] Bytes, string Token);

    private static bool CanMakeReadOnly { get; } = ProbeReadOnly();

    /// <summary>Read-only modes are ignored when the tests run as root, and do not exist on Windows.</summary>
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
    public async Task A_plan_that_fails_while_writing_leaves_the_tree_exactly_as_it_was()
    {
        var failures = new List<string>();
        int faulted = 0, clean = 0;
        for (int i = 0; i < Cases; i++)
        {
            var rng = new Random(Seed + i);
            var fault = (Fault)rng.Next(5);
            if (!CanMakeReadOnly && fault is Fault.ReadOnlyFile or Fault.ReadOnlyFolder) fault = Fault.FileWhereAFolderIsNeeded;
            var message = await RunCase(rng, fault, i);
            if (message is not null) failures.Add($"case {i} (seed {Seed + i}, {fault}): {message}");
            if (fault == Fault.None) clean++; else faulted++;
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
        Assert.True(faulted > 40 && clean > 10, $"the generator produced {faulted} faulted and {clean} clean plans");
    }

    private static async Task<string?> RunCase(Random rng, Fault fault, int index)
    {
        var root = WorkspacePaths.Canonicalize(Directory.CreateTempSubdirectory("thide-mutation-atomic-").FullName);
        try
        {
            var existing = Populate(root, rng);
            // Disposed before the finally deletes the folder: a watcher whose folder is already gone keeps
            // its inotify instance for the life of the process.
            await using var host = new WorkspaceHost();
            await host.LoadAsync(Path.Combine(root, "project.thconfig"));
            var engine = new MutationEngine(host);

            var (changes, expected) = Plan(root, rng, existing);
            int k = rng.Next(changes.Count + 1);
            if (fault != Fault.None) changes.Insert(k, FaultyChange(root, rng, fault));

            var before = Tree(root);
            var preview = await engine.ApplyAsync(new MutationPlan(changes), dryRun: true);
            if (Tree(root) != before) return "a dry run changed the tree";

            var result = await engine.ApplyAsync(new MutationPlan(changes), dryRun: false);
            var after = Tree(root);

            if (fault != Fault.None)
            {
                if (!preview.Ok) return $"the dry run refused the plan ({preview.Error!.Code}), so the fault was never reached";
                if (result.Ok) return "the faulty plan was reported as applied";
                if (result.Error!.Code is not (ToolErrorCodes.WriteFailed or ToolErrorCodes.UnrepresentableCharacter))
                    return $"refused with {result.Error.Code}: {result.Error.Message}";
                if (!result.Error.Message.Contains("Nothing was written", StringComparison.Ordinal))
                    return $"the rollback did not complete: {result.Error.Message}";
                if (after != before) return $"the tree changed although the plan failed at change {k + 1} of {changes.Count}:\n{Diff(before, after)}";
                return null;
            }

            if (!result.Ok) return $"a clean plan failed: {result.Error!.Code} {result.Error.Message}";
            foreach (var (relative, bytes) in expected)
            {
                var full = Path.Combine(root, relative);
                if (bytes is null) { if (!Directory.Exists(full)) return $"folder {relative} was not created"; continue; }
                if (!File.Exists(full)) return $"{relative} was not written";
                if (!File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes)) return $"{relative} holds different bytes from the plan's";
            }
            foreach (var e in existing.Where(e => !expected.ContainsKey(e.Relative)))
                if (!File.ReadAllBytes(Path.Combine(root, e.Relative)).AsSpan().SequenceEqual(e.Bytes)) return $"{e.Relative} changed although the plan left it alone";
            if (Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any()) return "a temporary file was left behind";
            return null;
        }
        finally
        {
            MakeWritable(root);
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- generator -----------------------------------------------------------------------------------

    private static readonly string[] Folders = ["caves", "caves/north", "caves/north/deep", "caves/south", "data"];

    private static List<Existing> Populate(string root, Random rng)
    {
        File.WriteAllText(Path.Combine(root, "project.thconfig"), "source caves/main.th\n");
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(root, f));
        Directory.CreateDirectory(Path.Combine(root, "locked"));
        File.WriteAllText(Path.Combine(root, "caves", "main.th"), "survey main\nendsurvey\n");
        File.WriteAllText(Path.Combine(root, "caves", "blocker.th"), "survey blocker\nendsurvey\n");
        File.WriteAllText(Path.Combine(root, "caves", "readonly.th"), "survey readonly\nendsurvey\n");

        var list = new List<Existing>();
        int n = rng.Next(2, 7);
        for (int i = 0; i < n; i++)
        {
            var token = $"token{i}x";
            var relative = $"{Folders[rng.Next(Folders.Length)]}/f{i}.th";
            var eol = rng.Next(2) == 0 ? "\n" : "\r\n";
            byte[] bytes = rng.Next(4) switch
            {
                // Latin-2 behind an encoding line: ł is 0xB3, ś is 0xB6.
                0 => [.. Encoding.ASCII.GetBytes($"encoding iso8859-2{eol}survey s{i} -title \""), 0xB3, 0xB6,
                      .. Encoding.ASCII.GetBytes($"\"{eol}  # {token}{eol}endsurvey{eol}")],
                1 => [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes($"survey s{i} -title \"Peștera\"{eol}  # {token}{eol}endsurvey{eol}")],
                _ => Encoding.UTF8.GetBytes($"survey s{i} -title \"Grotte\"{eol}  # {token}{eol}endsurvey{eol}"),
            };
            File.WriteAllBytes(Path.Combine(root, relative), bytes);
            list.Add(new Existing(relative, bytes, token));
        }
        return list;
    }

    private static (List<FileChange> Changes, Dictionary<string, byte[]?> Expected) Plan(string root, Random rng, List<Existing> existing)
    {
        var changes = new List<FileChange>();
        var expected = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        int count = rng.Next(1, 6);
        for (int c = 0; c < count; c++)
        {
            var fresh = $"new{c}/{(rng.Next(2) == 0 ? "" : "inner/")}n{c}.th";
            var freshPath = Path.Combine(root, $"{Folders[rng.Next(Folders.Length)]}/{fresh}");
            var freshRelative = Path.GetRelativePath(root, freshPath).Replace('\\', '/');
            var content = $"survey n{c}\nendsurvey\n";
            switch (rng.Next(6))
            {
                case 0 when existing.FirstOrDefault(e => !used.Contains(e.Relative)) is { } target:
                {
                    used.Add(target.Relative);
                    var full = Path.Combine(root, target.Relative);
                    var text = SourceFileIo.Read(full).Text;
                    int at = text.IndexOf(target.Token, StringComparison.Ordinal);
                    changes.Add(new EditFile(full, [new TextEdit(at, target.Token.Length, target.Token, "edited")]));
                    expected[target.Relative] = Replace(target.Bytes, target.Token, "edited");
                    break;
                }
                case 1 when existing.FirstOrDefault(e => !used.Contains(e.Relative)) is { } target:
                    used.Add(target.Relative);
                    changes.Add(new WriteFile(Path.Combine(root, target.Relative), content));
                    expected[target.Relative] = Encoding.UTF8.GetBytes(content);
                    break;
                case 2:
                    changes.Add(new WriteFile(freshPath, content));
                    expected[freshRelative] = Encoding.UTF8.GetBytes(content);
                    break;
                case 3:
                {
                    var source = existing[rng.Next(existing.Count)];
                    if (used.Contains(source.Relative)) goto default;
                    changes.Add(new CopyFile(freshPath, Path.Combine(root, source.Relative)));
                    expected[freshRelative] = source.Bytes;
                    break;
                }
                case 4:
                    changes.Add(new CreateDirectory(Path.GetDirectoryName(freshPath)!));
                    expected[Path.GetDirectoryName(freshRelative)!.Replace('\\', '/')] = null;
                    break;
                default:
                    changes.Add(new CreateFile(freshPath, content));
                    expected[freshRelative] = Encoding.UTF8.GetBytes(content);
                    break;
            }
        }
        return (changes, expected);
    }

    private static FileChange FaultyChange(string root, Random rng, Fault fault)
    {
        var deep = rng.Next(2) == 0 ? "" : "made/deeper/";
        switch (fault)
        {
            case Fault.FileWhereAFolderIsNeeded:
            {
                var target = Path.Combine(root, "caves", "blocker.th", deep + "x.th");
                return rng.Next(4) switch
                {
                    0 => new CreateFile(target, "survey x\nendsurvey\n"),
                    1 => new WriteFile(target, "survey x\nendsurvey\n"),
                    2 => new CopyFile(target, Path.Combine(root, "caves", "main.th")),
                    _ => new CreateDirectory(Path.Combine(root, "caves", "blocker.th", deep + "x")),
                };
            }
            case Fault.ReadOnlyFile:
            {
                var target = Path.Combine(root, "caves", "readonly.th");
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.GroupRead);
                return rng.Next(2) == 0
                    ? new EditFile(target, [new TextEdit(7, 8, "readonly", "changed!")])
                    : new WriteFile(target, "survey changed\nendsurvey\n");
            }
            case Fault.ReadOnlyFolder:
            {
                var locked = Path.Combine(root, "locked");
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                var target = Path.Combine(locked, deep + "x.th");
                return rng.Next(3) switch
                {
                    0 => new CreateFile(target, "survey x\nendsurvey\n"),
                    1 => new CopyFile(target, Path.Combine(root, "caves", "main.th")),
                    _ => new WriteFile(target, "survey x\nendsurvey\n"),
                };
            }
            default:
                // Latin-2 has no euro sign: the declared encoding cannot hold the content.
                return new CreateFile(Path.Combine(root, "data", deep + "euro.th"), "encoding iso8859-2\nsurvey e -title \"€\"\nendsurvey\n");
        }
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static byte[] Replace(byte[] bytes, string token, string with)
    {
        var t = Encoding.ASCII.GetBytes(token);
        int at = bytes.AsSpan().IndexOf(t);
        return [.. bytes[..at], .. Encoding.ASCII.GetBytes(with), .. bytes[(at + t.Length)..]];
    }

    /// <summary>Every file and folder under <paramref name="root"/>, with its mode and (for files) its digest.</summary>
    private static string Tree(string root)
    {
        var sb = new StringBuilder();
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false })
                     .OrderBy(e => e, StringComparer.Ordinal))
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
