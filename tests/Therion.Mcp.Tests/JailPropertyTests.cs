// The path jail and the protected config folder, as properties over generated paths rather than one
// example per escape. A temp workspace holds links to a folder inside it, to a folder and a file outside
// it, to its own `.thide`, a dangling link out, a link loop, and an outside folder that links back into
// `.thide`. Relative paths are assembled from those names and from `..`, `.`, empty segments,
// backslashes, case variants of `.thide`, trailing dots and spaces. The oracle resolves each path the way
// the operating system will when the file is opened — one readlink at a time — independently of the
// product's resolver.

using System.Text;
using Therion.Mcp.Mutations;

namespace Therion.Mcp.Tests;

public class JailPropertyTests : IDisposable
{
    private const int Seed = 20261103;
    private const int Cases = 600;
    private const int MutationCases = 160;

    private readonly string _scratch;
    private readonly string _root;
    private readonly string _outside;
    private readonly string _config;

    public JailPropertyTests()
    {
        _scratch = Path.GetFullPath(Directory.CreateTempSubdirectory("thide-jail-").FullName);
        _scratch = Oracle(_scratch)!;   // the temp folder itself may sit behind a link (macOS /var)
        _root = Path.Combine(_scratch, "work");
        _outside = Path.Combine(_scratch, "elsewhere");
        _config = Path.Combine(_root, ".thide");
        if (OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(Path.Combine(_root, "caves", "deep"));
        Directory.CreateDirectory(_config);
        Directory.CreateDirectory(Path.Combine(_outside, "secret"));
        File.WriteAllText(Path.Combine(_root, "project.thconfig"), "source caves/a.th\n");
        File.WriteAllText(Path.Combine(_root, "caves", "a.th"), "survey a\nendsurvey\n");
        File.WriteAllText(Path.Combine(_root, "caves", "deep", "b.th"), "survey b\nendsurvey\n");
        File.WriteAllText(Path.Combine(_config, "hooks.json"), "{ \"hooks\": [] }");
        File.WriteAllText(Path.Combine(_config, "tasks.json"), "{ \"tasks\": [] }");
        File.WriteAllText(Path.Combine(_outside, "secret", "s.th"), "survey s\nendsurvey\n");
        File.WriteAllText(Path.Combine(_outside, "x.th"), "survey x\nendsurvey\n");

        Directory.CreateSymbolicLink(Path.Combine(_root, "in"), Path.Combine(_root, "caves"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "inrel"), "caves/deep");
        Directory.CreateSymbolicLink(Path.Combine(_root, "out"), _outside);
        Directory.CreateSymbolicLink(Path.Combine(_root, "outrel"), "../elsewhere/secret");
        Directory.CreateSymbolicLink(Path.Combine(_root, "cfg"), _config);
        File.CreateSymbolicLink(Path.Combine(_root, "outfile.th"), Path.Combine(_outside, "x.th"));
        File.CreateSymbolicLink(Path.Combine(_root, "dangling.th"), Path.Combine(_outside, "not-there.th"));
        File.CreateSymbolicLink(Path.Combine(_root, "loop"), Path.Combine(_root, "loop"));
        Directory.CreateSymbolicLink(Path.Combine(_outside, "back"), _config);
        File.CreateSymbolicLink(Path.Combine(_root, "hooklink.json"), Path.Combine(_config, "hooks.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- generator -----------------------------------------------------------------------------------

    private static readonly string[] Segments =
    [
        "caves", "deep", "a.th", "b.th", "new.th", "..", ".", "", "in", "inrel", "out", "outrel", "cfg",
        "outfile.th", "dangling.th", "loop", "back", "secret", "s.th", ".thide", ".THIDE", ".Thide", ".thidE",
        "hooks.json", "tasks.json", "hooklink.json", ".thide-old", ".thidex", "elsewhere", "work",
        "x\\..\\..\\y", "..\\out", "name.", "name ", " ", "...",
    ];

    private string Generate(Random rng)
    {
        var parts = new List<string>();
        int n = rng.Next(1, 6);
        for (int i = 0; i < n; i++) parts.Add(Segments[rng.Next(Segments.Length)]);
        var path = string.Join(rng.Next(6) == 0 ? "//" : "/", parts);
        switch (rng.Next(12))
        {
            case 0: return _root + "/" + path;      // absolute, from inside
            case 1: return _outside + "/" + path;   // absolute, from outside
            case 2: return path + "/";
            default: return path;
        }
    }

    // ---- oracle --------------------------------------------------------------------------------------

    /// <summary>
    /// The file the operating system opens for <paramref name="path"/>: every link followed one readlink
    /// at a time, <c>..</c> taken physically, a missing tail appended as written. Null for a loop.
    /// </summary>
    private static string? Oracle(string path)
    {
        var pending = new Stack<string>(Enumerable.Reverse(Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries)));
        var current = "/";
        int hops = 0;
        while (pending.Count > 0)
        {
            var segment = pending.Pop();
            if (segment == ".") continue;
            if (segment == "..") { current = Path.GetDirectoryName(current) ?? "/"; continue; }
            var next = Path.Combine(current, segment);
            string? target = null;
            try { target = new FileInfo(next).LinkTarget; } catch (IOException) { }
            if (target is null) { current = next; continue; }
            if (++hops > 40) return null;
            var rest = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = rest.Length - 1; i >= 0; i--) pending.Push(rest[i]);
            if (target.StartsWith('/')) current = "/";
        }
        return current;
    }

    private static bool Under(string root, string candidate, StringComparison comparison) =>
        candidate.Equals(root, comparison) || candidate.StartsWith(root + "/", comparison);

    private static string Lexical(string root, string path) => Path.GetFullPath(Path.Combine(root, path));

    private static bool HasLoop(string root, string path)
    {
        try { return Oracle(Lexical(root, path)) is null; } catch (ArgumentException) { return false; }
    }

    // ---- properties ----------------------------------------------------------------------------------

    [Fact]
    public void The_jail_accepts_a_path_exactly_when_the_file_it_names_is_inside_the_root()
    {
        if (OperatingSystem.IsWindows()) return;   // links need privileges there; the jail is shared code
        var rng = new Random(Seed);
        var failures = new List<string>();
        for (int i = 0; i < Cases; i++)
        {
            var path = Generate(rng);
            if (string.IsNullOrWhiteSpace(path))
            {
                if (WorkspacePaths.TryResolve(_root, path, out _, out _)) failures.Add($"#{i} accepted the blank path '{path}'");
                continue;
            }
            if (HasLoop(_root, path)) continue;   // nothing can be opened through a loop; covered below
            var real = Oracle(Lexical(_root, path))!;
            bool inside = Under(_root, real, StringComparison.Ordinal);

            bool accepted = WorkspacePaths.TryResolve(_root, path, out var full, out var error);

            if (accepted != inside)
                failures.Add($"#{i} '{path}': accepted={accepted} but the file is {real} ({error})");
            else if (accepted && full != real)
                failures.Add($"#{i} '{path}': resolved to {full}, but the operating system opens {real}");
        }
        Assert.True(failures.Count == 0, $"seed {Seed}:\n" + string.Join("\n", failures.Take(15)));
    }

    [Fact]
    public void A_path_with_a_nul_character_is_always_refused()
    {
        var rng = new Random(Seed + 1);
        for (int i = 0; i < 100; i++)
        {
            var path = Generate(rng);
            var cut = rng.Next(path.Length + 1);
            var poisoned = path[..cut] + '\0' + path[cut..];
            Assert.False(WorkspacePaths.TryResolve(_root, poisoned, out _, out _), $"seed {Seed + 1} #{i}: accepted a NUL in '{path}'");
        }
    }

    [Fact]
    public void The_config_folder_is_recognised_however_it_is_spelled_or_reached()
    {
        if (OperatingSystem.IsWindows()) return;
        var rng = new Random(Seed + 2);
        var failures = new List<string>();
        for (int i = 0; i < Cases; i++)
        {
            var path = Generate(rng);
            if (HasLoop(_root, path)) continue;
            var lexical = Lexical(_root, path);
            var real = Oracle(lexical)!;
            // Protected: anything whose real file lies in .thide, or whose name puts it there on a volume
            // that ignores case. Nothing else is: `.thide-old` and `.thidex` are ordinary folders.
            bool expected = Under(_config, real, StringComparison.Ordinal)
                            || Under(_config, lexical, StringComparison.OrdinalIgnoreCase)
                            || Under(_config, real, StringComparison.OrdinalIgnoreCase);

            if (WorkspacePaths.IsProjectConfig(_root, lexical) != expected)
                failures.Add($"#{i} '{path}' ({lexical} -> {real}): expected protected={expected}");
            if (WorkspacePaths.TryResolve(_root, path, out var full, out _) && WorkspacePaths.IsProjectConfig(_root, full) != expected)
                failures.Add($"#{i} '{path}' resolved to {full}: expected protected={expected}");
        }
        Assert.True(failures.Count == 0, $"seed {Seed + 2}:\n" + string.Join("\n", failures.Take(15)));
    }

    [Fact]
    public async Task No_mutation_reaches_outside_the_root_or_into_the_config_folder()
    {
        if (OperatingSystem.IsWindows()) return;
        var host = new WorkspaceHost();
        await host.LoadAsync(Path.Combine(_root, "project.thconfig"));
        var engine = new MutationEngine(host);
        var guarded = Snapshot();
        var rng = new Random(Seed + 3);
        var failures = new List<string>();
        int refused = 0, applied = 0;

        for (int i = 0; i < MutationCases; i++)
        {
            var path = Generate(rng);
            string lexical;
            try { lexical = Lexical(_root, path); } catch (ArgumentException) { continue; }
            FileChange change = (i % 4) switch
            {
                0 => new WriteFile(lexical, "survey pwned\nendsurvey\n"),
                1 => new CreateFile(lexical, "survey pwned\nendsurvey\n"),
                2 => new CreateDirectory(lexical),
                _ => new CopyFile(lexical, Path.Combine(_root, "caves", "a.th")),
            };
            var real = HasLoop(_root, path) ? null : Oracle(lexical);
            bool forbidden = real is null || !Under(_root, real, StringComparison.Ordinal) || Under(_config, real, StringComparison.OrdinalIgnoreCase)
                             || Under(_config, lexical, StringComparison.OrdinalIgnoreCase);

            ToolResult<MutationResult> result;
            try { result = await engine.ApplyAsync(new MutationPlan([change]), dryRun: false); }
            catch (Exception ex) { failures.Add($"#{i} {change.GetType().Name} '{path}': threw {ex.GetType().Name}: {ex.Message}"); continue; }

            if (forbidden) refused += result.Ok ? 0 : 1; else if (result.Ok) applied++;
            if (forbidden && real is not null && result.Ok)
                failures.Add($"#{i} {change.GetType().Name} '{path}' (-> {real}) was applied");
            if (forbidden && real is not null && !result.Ok
                && result.Error!.Code is not (ToolErrorCodes.PathOutsideWorkspace or ToolErrorCodes.ProtectedPath))
                failures.Add($"#{i} {change.GetType().Name} '{path}' (-> {real}) refused with {result.Error.Code}, not by the jail");

            var now = Snapshot();
            if (Describe(now) != Describe(guarded))
            {
                failures.Add($"#{i} {change.GetType().Name} '{path}' changed the outside or the config folder");
                guarded = now;
            }
        }
        Assert.True(failures.Count == 0, $"seed {Seed + 3}:\n" + string.Join("\n", failures.Take(15)));
        Assert.True(refused > 30 && applied > 10, $"the generator gave {refused} refused escapes and {applied} applied inside writes");
    }

    /// <summary>Every entry under the outside folder and under .thide, with its bytes (links as links).</summary>
    private SortedDictionary<string, string> Snapshot()
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Walk(string dir, string label)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                var info = new FileInfo(entry);
                var key = label + "/" + Path.GetFileName(entry);
                if (info.LinkTarget is { } target) map[key] = "link:" + target;
                else if (Directory.Exists(entry)) { map[key] = "dir"; Walk(entry, key); }
                else map[key] = Convert.ToHexString(File.ReadAllBytes(entry));
            }
        }
        Walk(_outside, "elsewhere");
        Walk(_config, ".thide");
        foreach (var variant in Directory.EnumerateDirectories(_root).Where(d =>
                     Path.GetFileName(d).Equals(".thide", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(d) != ".thide"))
            map["variant:" + Path.GetFileName(variant)] = "dir";
        return map;
    }

    private static string Describe(SortedDictionary<string, string> map)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in map) sb.Append(k).Append('=').Append(v).Append('\n');
        return sb.ToString();
    }
}
