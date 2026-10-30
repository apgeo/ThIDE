// `therion-cli format` — the editor's Format Document, headless: print a file re-indented, rewrite
// files in place (--write, keeping each file's encoding), or report the ones that would change
// (--check, exit 1 on drift) for pre-commit hooks and CI.

using System.Globalization;
using Therion.Core;
using Therion.Workspace;
using Therion.Workspace.Editing;
using Therion.Workspace.Formatting;

namespace Therion.Cli;

public static class FormatVerb
{
    private static readonly string[] Extensions = [".th", ".th2", ".thconfig", ".thc"];

    /// <summary>
    /// <c>format &lt;file|folder&gt;… [--write | --check] [--indent N|tab]</c>. Returns 0 ok (nothing to
    /// change, for --check), 1 drift found (--check) or a write failed, 2 usage error or missing file.
    /// </summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        bool write = false, check = false;
        string unit = "  ";
        var targets = new List<string>();
        for (int i = 1; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--write": write = true; break;
                case "--check": check = true; break;
                case "--indent" when i + 1 < args.Count:
                    var v = args[++i];
                    if (v.Equals("tab", StringComparison.OrdinalIgnoreCase)) unit = "\t";
                    else if (int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is > 0 and <= 16) unit = new string(' ', n);
                    else { stderr.WriteLine($"error: --indent expects a number of spaces (1–16) or 'tab', got '{v}'."); return 2; }
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal)) { stderr.WriteLine($"error: unknown option '{args[i]}'."); return 2; }
                    targets.Add(args[i]);
                    break;
            }
        }
        if (targets.Count == 0) { stderr.WriteLine("error: 'format' requires a file or folder."); return 2; }
        if (write && check) { stderr.WriteLine("error: use --write or --check, not both."); return 2; }

        var files = new List<string>();
        foreach (var t in targets)
        {
            if (Directory.Exists(t)) files.AddRange(Expand(t));
            else if (File.Exists(t)) files.Add(Path.GetFullPath(t));
            else { stderr.WriteLine($"error: file not found: {t}"); return 2; }
        }
        if (!write && !check && files.Count != 1)
        {
            stderr.WriteLine("error: printing the formatted text takes one file; use --write or --check for several.");
            return 2;
        }

        int drift = 0, failed = 0, skipped = 0;
        foreach (var file in files.Distinct(FilePaths.Comparer))
        {
            SourceFile source;
            try { source = SourceFileIo.Read(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"error: {Display(file)}: {ex.Message}");
                failed++;
                continue;
            }

            // The same rule as Format all files: a file the parser gave up on would be re-indented
            // around what it misunderstood.
            var parsed = TherionWorkspace.ParseText(file, source.Text);
            if (parsed.Value is null || parsed.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error || d.Code.Value == Therion.Workspace.Fixes.CodeFixes.UnterminatedBlock))
            {
                stderr.WriteLine($"warning: {Display(file)}: skipped, it has parse errors (see 'validate').");
                skipped++;
                if (!write && !check) return 1;
                continue;
            }

            var formatted = TherionReindenter.Format(source.Text, unit);
            bool changed = !string.Equals(formatted, source.Text, StringComparison.Ordinal);
            if (!write && !check) { stdout.Write(formatted); return 0; }
            if (!changed) continue;
            drift++;
            if (check) { stdout.WriteLine($"would reformat {Display(file)}"); continue; }
            try
            {
                SourceFileIo.Write(source, formatted);
                stderr.WriteLine($"formatted {Display(file)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnrepresentableCharacterException)
            {
                stderr.WriteLine($"error: {Display(file)}: {ex.Message}");
                failed++;
            }
        }

        var skippedNote = skipped > 0 ? $", {skipped} skipped" : "";
        if (check)
        {
            stderr.WriteLine(drift == 0
                ? $"{files.Count} file(s) already formatted{skippedNote}."
                : $"{drift} of {files.Count} file(s) would be reformatted{skippedNote}. Run 'therion-cli format --write' to fix.");
            return drift > 0 || failed > 0 ? 1 : 0;
        }
        stderr.WriteLine($"{drift} file(s) reformatted{skippedNote}.");
        return failed > 0 ? 1 : 0;
    }

    /// <summary>Therion sources under <paramref name="folder"/>, skipping dot folders (.git, .thide) and ThIDE's temporary configs.</summary>
    public static IEnumerable<string> Expand(string folder)
    {
        var root = Path.GetFullPath(folder);
        var pending = new Stack<string>([root]);
        var found = new List<string>();
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
                if (!Path.GetFileName(sub).StartsWith('.')) pending.Push(sub);
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith(".thide-", StringComparison.OrdinalIgnoreCase)) continue;
                if (Extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) found.Add(f);
            }
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static string Display(string path)
    {
        var rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
        return rel.StartsWith("..", StringComparison.Ordinal) ? path : rel.Replace('\\', '/');
    }
}
