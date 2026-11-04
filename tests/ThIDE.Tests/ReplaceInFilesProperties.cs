// Replace in Files over generated folders: text files in UTF-8 (with and without a BOM), Latin-1 and
// Latin-2 behind an `encoding` line, LF, CRLF and mixed line ends; binaries holding NUL bytes; files
// whose bytes their encoding cannot decode; case-variant names (Linux); a folder symlinked to a
// directory outside the root (POSIX). Literal and regex queries (anchors, \s, (?s), groups, a query
// spanning two lines), with match case and whole word, replace through the view model, then the
// files are edited elsewhere or not and the replace is undone (and redone).
//
// What must hold for every case:
// - every match Find All listed is replaced, and nothing else: the new text is the old text with the
//   listed (offset, length) spans swapped for the replacement;
// - the bytes between the matches are the old bytes, in the file's own encoding and BOM;
// - binaries, undecodable files and files behind the symlink are byte-identical and never listed;
// - a replacement one file's encoding cannot hold changes no file;
// - Undo puts back the exact old bytes, or, when a file was changed after the replace, refuses and
//   changes nothing; Redo puts back the exact replaced bytes.
//
// Seeds are fixed (THIDE_PROPERTY_SEED overrides the base); a failure names the seed and the case.

using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Therion.Workspace.Editing;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

public class ReplaceInFilesProperties
{
    private const int DefaultSeed = 0x5E12_0001;
    private const int Cases = 60;
    private const string Mask = "*.th;*.thconfig";

    private static int BaseSeed =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_SEED"), out var s) ? s : DefaultSeed;

    static ReplaceInFilesProperties() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private enum Kind { Text, Binary, Undecodable, Outside }

    private sealed record GenFile(string Path, Kind Kind, byte[] Bytes);

    private sealed record Query(string Find, bool Regex, bool MatchCase, bool WholeWord, string Replacement)
    {
        public override string ToString() =>
            $"{(Regex ? "regex" : "literal")} '{Find}' → '{Replacement}'{(MatchCase ? " case" : "")}{(WholeWord ? " word" : "")}";
    }

    // (encoding line, encoding, BOM, a non-ASCII text the encoding holds)
    private static readonly (string? Line, int CodePage, bool Bom, string NonAscii)[] Encodings =
    [
        (null, 65001, false, "ș→é"),
        (null, 65001, true, "ăîé"),
        ("encoding iso8859-1", 28591, false, "éüñ"),
        ("encoding iso8859-2", 28592, false, "łčé"),
    ];

    private static readonly string[] LineTemplates =
    [
        "survey cave1 -title \"Hall {0}\"",
        "  station A1 \"hall\"",
        "  1 2 10.5 120 -5",
        "# note {0} Cave1",
        "  station  A12 \"Hall of {0}\"",
        "    hall cave1 hall",
        "\tCAVE1\t# station",
        "",
        "   ",
        "  A1 A2 10.5 0 0 # cave1",
        "  station B2 \"Hall é\"",
        "endsurvey",
    ];

    private static readonly string[] LiteralFinds = ["cave1", "A1", "hall", "station", "10.5", "#", "é", "Hall é"];
    private static readonly string[] RegexFinds =
    [
        @"^\s*station", @"cave1$", @"\s+#", @"(?s)hall.", @"A(\d+)", @"\bhall\b", @"^#.*$",
        @"[0-9]+\.[0-9]+", @"hall\ncave1", @"cave1\r?$", @"^\s*$", @"(?<=station )A\d",
    ];
    private static readonly string[] LiteralReplacements = ["B1", "", "$0", "é", "ș", "x\ty"];
    private static readonly string[] RegexReplacements = ["X", "", "[$0]", "<$1>", "é", "ș"];

    [Fact]
    public async Task Replace_all_changes_exactly_the_previewed_matches_and_undo_restores_the_bytes()
    {
        var failures = new List<string>();
        var reach = new Dictionary<string, int>();
        void Reached(string what) => reach[what] = reach.GetValueOrDefault(what) + 1;

        for (int i = 0; i < Cases; i++)
        {
            int seed = BaseSeed + i;
            string? message;
            try { message = await RunCase(seed, Reached); }
            catch (Exception ex) { message = "threw " + ex; }
            if (message is not null) failures.Add($"seed {seed} (base {BaseSeed}, case {i}): {message}");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(8)));
        // The generator must have reached the shapes the property is about.
        foreach (var shape in new[] { "replaced", "crlf-replaced", "refused-encoding", "binary", "undecodable", "undo-done", "undo-refused", "no-match" })
            Assert.True(reach.GetValueOrDefault(shape) > 0, $"no case reached '{shape}' (reach: {string.Join(", ", reach.Select(kv => $"{kv.Key}={kv.Value}"))})");
        if (!OperatingSystem.IsWindows()) Assert.True(reach.GetValueOrDefault("symlink") > 0, "no case had a symlinked folder");
        if (OperatingSystem.IsLinux()) Assert.True(reach.GetValueOrDefault("case-variant") > 0, "no case had case-variant names");
    }

    private static async Task<string?> RunCase(int seed, Action<string> reached)
    {
        var rng = new Random(seed);
        var root = Directory.CreateTempSubdirectory("thide-rif-prop-").FullName;
        var outside = Directory.CreateTempSubdirectory("thide-rif-out-").FullName;
        try
        {
            var files = Populate(root, outside, rng, reached);
            var query = MakeQuery(rng);

            var docs = new ObservableCollection<FileDocumentViewModel>();
            var service = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name == "get_Documents" ? docs : null);
            var vm = new ReplaceInFilesViewModel(service)
            {
                Scope = ReplaceInFilesViewModel.ScopeDirectory,
                Directory = root,
                FileMask = Mask,
                Query = query.Find,
                Replacement = query.Replacement,
                UseRegex = query.Regex,
                MatchCase = query.MatchCase,
                WholeWord = query.WholeWord,
            };

            await vm.FindAllCommand.ExecuteAsync(null);
            var hits = vm.Results.ToList();
            var regex = BuildRegex(query);

            // Only the text files under the root may be listed.
            var byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
            foreach (var hit in hits)
            {
                var full = Path.GetFullPath(hit.FilePath);
                if (!byPath.TryGetValue(full, out var f)) return $"{query}: listed a file the case did not make: {hit.FilePath}";
                if (f.Kind != Kind.Text) return $"{query}: listed {f.Kind} file {Rel(root, full)}";
            }

            // The expected result of each text file, from the preview alone.
            var expected = new Dictionary<string, (string Text, byte[] Bytes)>(StringComparer.Ordinal);
            bool refusal = false;
            foreach (var f in files.Where(f => f.Kind == Kind.Text))
            {
                var listed = hits.Where(h => Path.GetFullPath(h.FilePath) == f.Path).Select(h => h.Span).OrderBy(s => s.StartOffset).ToList();
                if (listed.Count == 0) continue;
                var file = SourceFileIo.Read(f.Path, f.Bytes);
                var strict = Encoding.GetEncoding(file.Encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

                var newText = new StringBuilder();
                var newBytes = new List<byte>(file.Bom);
                var oldBytes = new List<byte>(file.Bom);
                int at = 0;
                foreach (var span in listed)
                {
                    if (span.StartOffset < at) return $"{query}: overlapping matches listed in {Rel(root, f.Path)}";
                    var (lineStart, lineEnd) = LineAround(file.Text, span.StartOffset);
                    var line = file.Text[lineStart..lineEnd];
                    var m = regex.Match(line, span.StartOffset - lineStart);
                    if (!m.Success || m.Index != span.StartOffset - lineStart || m.Length != span.Length)
                        return $"{query}: a listed match in {Rel(root, f.Path)} at {span.StartOffset} is not a match of the query";
                    var replacement = query.Regex ? m.Result(query.Replacement) : query.Replacement;
                    var gap = file.Text[at..span.StartOffset];
                    newText.Append(gap).Append(replacement);
                    newBytes.AddRange(strict.GetBytes(gap));
                    oldBytes.AddRange(strict.GetBytes(gap));
                    oldBytes.AddRange(strict.GetBytes(m.Value));
                    try { newBytes.AddRange(strict.GetBytes(replacement)); }
                    catch (EncoderFallbackException) { refusal = true; }
                    at = span.StartOffset + span.Length;
                }
                var tail = file.Text[at..];
                newText.Append(tail);
                newBytes.AddRange(strict.GetBytes(tail));
                oldBytes.AddRange(strict.GetBytes(tail));
                if (!oldBytes.SequenceEqual(f.Bytes)) return $"generator: {Rel(root, f.Path)} does not re-encode to its own bytes";
                if (newText.ToString() != file.Text) expected[f.Path] = (newText.ToString(), newBytes.ToArray());
            }

            await vm.ReplaceAllCommand.ExecuteAsync(null);
            var afterReplace = files.ToDictionary(f => f.Path, f => File.ReadAllBytes(f.Path), StringComparer.Ordinal);

            if (refusal)
            {
                reached("refused-encoding");
                foreach (var f in files)
                    if (!afterReplace[f.Path].AsSpan().SequenceEqual(f.Bytes))
                        return $"{query}: a replacement one file's encoding cannot hold still changed {Rel(root, f.Path)}";
                if (string.IsNullOrEmpty(vm.Status)) return $"{query}: the refused replace said nothing";
                return null;
            }

            foreach (var f in files)
            {
                var now = afterReplace[f.Path];
                if (expected.TryGetValue(f.Path, out var want))
                {
                    var read = SourceFileIo.Read(f.Path, now);
                    if (read.Text != want.Text)
                        return $"{query}: {Rel(root, f.Path)} does not hold exactly the previewed replacements";
                    if (!now.AsSpan().SequenceEqual(want.Bytes))
                        return $"{query}: {Rel(root, f.Path)} changed bytes outside the matches (encoding or BOM not kept)";
                    reached("replaced");
                    if (TextOf(f).Contains("\r\n")) reached("crlf-replaced");
                }
                else if (!now.AsSpan().SequenceEqual(f.Bytes))
                    return $"{query}: {f.Kind} file {Rel(root, f.Path)} changed although it had no listed match";
            }
            if (expected.Count == 0) { reached("no-match"); return null; }

            // Undo, with or without a later edit to one of the replaced files.
            if (!vm.CanUndo) return $"{query}: nothing to undo after a replace that changed {expected.Count} file(s)";
            string? editedLater = rng.Next(3) == 0 ? expected.Keys.ElementAt(rng.Next(expected.Count)) : null;
            byte[]? editedBytes = null;
            if (editedLater is not null)
            {
                editedBytes = [.. afterReplace[editedLater], .. "# edited later\n"u8.ToArray()];
                File.WriteAllBytes(editedLater, editedBytes);
            }

            await vm.UndoCommand.ExecuteAsync(null);

            if (editedLater is not null)
            {
                reached("undo-refused");
                foreach (var f in files)
                {
                    var want = f.Path == editedLater ? editedBytes! : afterReplace[f.Path];
                    if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(want))
                        return $"{query}: Undo after {Rel(root, editedLater)} was edited elsewhere changed {Rel(root, f.Path)}";
                }
                return null;
            }

            reached("undo-done");
            foreach (var f in files)
                if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(f.Bytes))
                    return $"{query}: Undo did not restore the exact bytes of {Rel(root, f.Path)}";

            if (!vm.CanRedo) return $"{query}: nothing to redo after Undo";
            await vm.RedoCommand.ExecuteAsync(null);
            foreach (var f in files)
                if (!File.ReadAllBytes(f.Path).AsSpan().SequenceEqual(afterReplace[f.Path]))
                    return $"{query}: Redo did not restore the replaced bytes of {Rel(root, f.Path)}";
            return null;

            static string TextOf(GenFile g) => SourceFileIo.Read(g.Path, g.Bytes).Text;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(outside, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---- generator -------------------------------------------------------------------------

    private static List<GenFile> Populate(string root, string outside, Random rng, Action<string> reached)
    {
        var files = new List<GenFile>();
        var names = new List<string> { "a.th", "b.th", "cave.th", "sub/c.th", "sub/deep/d.th", "main.thconfig" };
        int textCount = rng.Next(2, 6);
        for (int k = 0; k < textCount; k++)
        {
            var name = names[rng.Next(names.Count)];
            names.Remove(name);
            files.Add(WriteText(root, name, rng));
            if (name == "cave.th" && OperatingSystem.IsLinux() && rng.Next(2) == 0)
            {
                files.Add(WriteText(root, "Cave.th", rng));
                reached("case-variant");
            }
        }
        if (rng.Next(3) > 0)
        {
            // Text with the query's words in it, and NUL bytes: a binary, whatever its extension.
            var bytes = Encoding.ASCII.GetBytes("survey cave1\n  station A1 \"hall\"\n# 10.5 é\n").Concat(new byte[] { 0, 1, 2, 0 })
                .Concat(Encoding.ASCII.GetBytes("hall cave1 A1\n")).ToArray();
            files.Add(WriteBytes(root, names.Count > 0 && rng.Next(2) == 0 ? "scan.th" : "sub/scan.th", Kind.Binary, bytes));
            reached("binary");
        }
        if (rng.Next(3) > 0)
        {
            // UTF-8 by default, with bytes UTF-8 cannot decode: rewriting it would lose them.
            var bytes = Encoding.ASCII.GetBytes("survey cave1\n  station A1 \"hall ").Concat(new byte[] { 0xFF, 0xC3, 0x28 })
                .Concat(Encoding.ASCII.GetBytes("\"\n  1 2 10.5 0 0 # cave1\nendsurvey\n")).ToArray();
            files.Add(WriteBytes(root, "old.th", Kind.Undecodable, bytes));
            reached("undecodable");
        }
        if (!OperatingSystem.IsWindows() && rng.Next(2) == 0)
        {
            var bytes = Encoding.UTF8.GetBytes("survey cave1\n  station A1 \"hall\"\n  1 2 10.5 0 0 # cave1\nendsurvey\n");
            files.Add(WriteBytes(outside, "o.th", Kind.Outside, bytes));
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
            reached("symlink");
        }
        return files;
    }

    private static GenFile WriteText(string root, string name, Random rng)
    {
        var (line, codePage, bom, nonAscii) = Encodings[rng.Next(Encodings.Length)];
        var endings = rng.Next(3);   // 0 LF, 1 CRLF, 2 mixed
        var sb = new StringBuilder();
        if (line is not null) sb.Append(line).Append('\n');
        int count = rng.Next(3, 10);
        for (int k = 0; k < count; k++)
        {
            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, LineTemplates[rng.Next(LineTemplates.Length)], nonAscii));
            bool last = k == count - 1;
            if (last && rng.Next(5) == 0) break;   // no newline at the end of the file
            sb.Append(endings == 1 || endings == 2 && rng.Next(2) == 0 ? "\r\n" : "\n");
        }
        var encoding = Encoding.GetEncoding(codePage);
        byte[] bytes = [.. bom ? Encoding.UTF8.GetPreamble() : [], .. encoding.GetBytes(sb.ToString())];
        return WriteBytes(root, name, Kind.Text, bytes);
    }

    private static GenFile WriteBytes(string root, string name, Kind kind, byte[] bytes)
    {
        var full = Path.GetFullPath(Path.Combine(root, name));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return new GenFile(full, kind, bytes);
    }

    private static Query MakeQuery(Random rng)
    {
        bool regex = rng.Next(2) == 0;
        var find = regex ? RegexFinds[rng.Next(RegexFinds.Length)] : LiteralFinds[rng.Next(LiteralFinds.Length)];
        var replacement = regex ? RegexReplacements[rng.Next(RegexReplacements.Length)] : LiteralReplacements[rng.Next(LiteralReplacements.Length)];
        return new Query(find, regex, MatchCase: rng.Next(2) == 0, WholeWord: rng.Next(4) == 0, replacement);
    }

    // What the options mean, independent of the panel: a literal is matched as written, whole word
    // puts a word boundary on each side, and without match case the case is ignored.
    private static Regex BuildRegex(Query q)
    {
        var pattern = q.Regex ? q.Find : Regex.Escape(q.Find);
        if (q.WholeWord) pattern = $@"\b(?:{pattern})\b";
        return new Regex(pattern, q.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
    }

    // The line holding text[offset], without its terminator ('\n', and a '\r' before it).
    private static (int Start, int End) LineAround(string text, int offset)
    {
        int start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int nl = text.IndexOf('\n', offset);
        int end = nl < 0 ? text.Length : nl;
        if (end > start && text[end - 1] == '\r') end--;
        return (start, end);
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path);
}
