// One fixture matrix for every code path that writes a Therion source file. Each writer's test class
// crosses its own adapter with these fixtures and asks the same question: the write either happens
// exactly (bytes == original BOM + the text strictly encoded in the file's own encoding) or is refused
// and leaves the file byte-identical — never a third outcome. The fixtures are invented.

using System.Text;
using Therion.Syntax;
using Therion.Workspace.Editing;
using Therion.Workspace.Formatting;

namespace Therion.Testing;

/// <summary>An encoding a Therion file can be stored in, with a sample it can hold and one it cannot.</summary>
/// <param name="Directive">The name on the file's <c>encoding</c> line, or null for a file without one.</param>
/// <param name="Sample">Text the encoding holds, written into the fixture's comment, title and edit.</param>
/// <param name="Foreign">A character the encoding cannot hold, or null when it holds every character.</param>
internal sealed record TherionEncodingCase(string Label, string? Directive, int CodePage, byte[] Bom, string Sample, string? Foreign)
{
    public override string ToString() => Label;

    /// <summary>
    /// The encoding with exception fallbacks both ways. Legacy code pages are obtained through
    /// <see cref="EncodingResolver"/>, so the test registers no provider of its own (one would hide a
    /// resolver that forgot to).
    /// </summary>
    public Encoding Strict()
    {
        var encoding = CodePage switch
        {
            1200 => Encoding.Unicode,
            1201 => Encoding.BigEndianUnicode,
            65001 => new UTF8Encoding(false),
            _ => EncodingResolver.DetectEncoding(Encoding.ASCII.GetBytes($"encoding {Directive}\n"))
                 ?? throw new InvalidOperationException($"the resolver does not know '{Directive}'"),
        };
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        return strict;
    }

    /// <summary>As <see cref="Strict"/>, but undecodable bytes become U+FFFD.</summary>
    public Encoding Replacing()
    {
        var replacing = (Encoding)Strict().Clone();
        replacing.DecoderFallback = new DecoderReplacementFallback("�");
        return replacing;
    }

    /// <summary>Bytes that are not valid in this encoding, or null when every byte sequence decodes.</summary>
    public byte[]? InvalidBytes()
    {
        if (CodePage is 1200 or 1201) return null;
        if (CodePage == 65001) return [0xC3, 0x28];   // a lead byte followed by a non-continuation byte
        var strict = Strict();
        for (int b = 0x80; b <= 0xFF; b++)
        {
            try { strict.GetString([(byte)b]); }
            catch (DecoderFallbackException) { return [(byte)b]; }
        }
        return null;
    }
}

internal static class TherionEncodings
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Every name <c>therion --print-encodings</c> lists (spelled in mixed case, as Therion accepts any
    /// case), plus UTF-8 without a directive, with a BOM, and UTF-16 in both byte orders.
    /// </summary>
    public static IReadOnlyList<TherionEncodingCase> All { get; } =
    [
        new("ascii", "ASCII", 20127, [], "Grotte", "é"),
        new("cp1250", "cp1250", 1250, [], "Jaskinia łśćŽ", "Ж"),
        new("cp1251", "CP1251", 1251, [], "Пещера", "é"),
        new("cp1252", "Cp1252", 1252, [], "Grotte éàüœ€", "ł"),
        new("cp1253", "cp1253", 1253, [], "Σπήλαιο", "Ж"),
        new("iso8859-1", "ISO8859-1", 28591, [], "Höhle éàüß", "€"),
        new("iso8859-2", "iso8859-2", 28592, [], "Jaskinia łśčŽ", "€"),
        new("iso8859-5", "Iso8859-5", 28595, [], "Пещера", "é"),
        new("iso8859-7", "iso8859-7", 28597, [], "Σπήλαιο", "Ж"),
        new("utf-8", "UTF-8", 65001, [], "Peșteră 𝄞 é", null),
        new("utf-8-plain", null, 65001, [], "Peșteră 𝄞 é", null),
        new("utf-8-bom", null, 65001, Utf8Bom, "Peșteră 𝄞 é", null),
        new("utf-16le-bom", null, 1200, [0xFF, 0xFE], "Peșteră 𝄞 é", null),
        new("utf-16be-bom", null, 1201, [0xFE, 0xFF], "Peșteră 𝄞 é", null),
    ];

    public static TherionEncodingCase Get(string label) => All.Single(e => e.Label == label);
}

internal enum FixtureKind
{
    /// <summary>A clean file; the edit stays within the encoding.</summary>
    Plain,
    /// <summary>A clean file; the text edit brings in a character the encoding cannot hold.</summary>
    Unencodable,
    /// <summary>The file holds bytes its encoding cannot decode.</summary>
    Lossy,
    /// <summary>The path is a symbolic link to the file, which lives in another folder.</summary>
    Symlink,
    /// <summary>The file has mode 0640.</summary>
    Mode0640,
    /// <summary>The file is read-only (0444).</summary>
    ReadOnlyFile,
    /// <summary>The file is writable but its folder is not (0555), so no temporary file can be made beside it.</summary>
    ReadOnlyFolder,
    /// <summary>A sibling whose name differs only in case (case-sensitive file systems only).</summary>
    CaseSibling,
}

/// <summary>What a writer does to the fixture: a text edit (MARK becomes the replacement) or a re-indent.</summary>
internal enum WriterKind { TextEdit, Reindent }

/// <summary>One cell's file: its bytes, the text they decode to, and the edit a text-edit writer makes.</summary>
internal sealed record WriteFixture(string Name, TherionEncodingCase Encoding, string Endings, FixtureKind Kind, byte[] Bytes, string Text, string Replace)
{
    /// <summary>The text a text-edit writer replaces; it occurs exactly once in <see cref="Text"/>.</summary>
    public const string Find = "MARK";

    public override string ToString() => Name;

    public string EditedText => Text.Replace(Find, Replace, StringComparison.Ordinal);

    /// <summary>The text a re-indenting writer produces (each line keeps its own terminator).</summary>
    public string ReindentedText => TherionReindenter.Format(Text, "  ");

    public string ExpectedText(WriterKind writer) => writer == WriterKind.TextEdit ? EditedText : ReindentedText;

    /// <summary>Whether a correct writer must refuse this cell rather than write it.</summary>
    public bool MustBeRefused(WriterKind writer) =>
        Kind is FixtureKind.Lossy or FixtureKind.ReadOnlyFile || (Kind == FixtureKind.Unencodable && writer == WriterKind.TextEdit);

    /// <summary>The cell needs POSIX links or permissions.</summary>
    public bool NeedsPosix => Kind is FixtureKind.Symlink or FixtureKind.Mode0640 or FixtureKind.ReadOnlyFile
        or FixtureKind.ReadOnlyFolder or FixtureKind.CaseSibling;
}

internal static class WriteFixtures
{
    private static readonly Lazy<IReadOnlyDictionary<string, WriteFixture>> Catalogue = new(Build);

    public static IReadOnlyCollection<WriteFixture> All => Catalogue.Value.Values.ToList();

    public static WriteFixture Get(string name) => Catalogue.Value[name];

    /// <summary>The names of the fixtures that pass <paramref name="filter"/>, as theory data.</summary>
    public static TheoryData<string> Names(Func<WriteFixture, bool>? filter = null)
    {
        var data = new TheoryData<string>();
        foreach (var f in Catalogue.Value.Values.OrderBy(f => f.Name, StringComparer.Ordinal))
            if (filter is null || filter(f)) data.Add(f.Name);
        return data;
    }

    /// <summary>The fixture's lines, without terminators. Not indented, so a re-indent always changes it.</summary>
    public static IReadOnlyList<string> Lines(TherionEncodingCase e, string commentFiller = "")
    {
        var lines = new List<string>();
        if (e.Directive is not null) lines.Add($"encoding {e.Directive}");
        lines.Add($"# write-path fixture {commentFiller}{e.Sample}");
        lines.Add($"survey wp -title \"Grotte {e.Sample}\"");
        lines.Add("centreline");
        lines.Add("data normal from to length compass clino");
        lines.Add("1 2 10.0 90 0");
        lines.Add("2 3 5.5 180 -10");
        lines.Add("endcentreline");
        lines.Add($"# note {WriteFixture.Find}");
        lines.Add("endsurvey");
        return lines;
    }

    /// <summary>Joins lines with LF, CRLF, or alternating CRLF/LF without a final terminator ("mixed").</summary>
    public static string Join(IReadOnlyList<string> lines, string endings)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            sb.Append(lines[i]);
            sb.Append(endings switch
            {
                "lf" => "\n",
                "crlf" => "\r\n",
                _ => i == lines.Count - 1 ? "" : i % 2 == 0 ? "\r\n" : "\n",
            });
        }
        return sb.ToString();
    }

    private static string EditFor(TherionEncodingCase e) =>
        "MARQ" + string.Concat(e.Sample.EnumerateRunes().Where(r => r.Value > 0x7F).Take(3));

    private static IReadOnlyDictionary<string, WriteFixture> Build()
    {
        var all = new Dictionary<string, WriteFixture>(StringComparer.Ordinal);
        void Add(WriteFixture f) => all.Add(f.Name, f);

        foreach (var e in TherionEncodings.All)
        {
            foreach (var endings in new[] { "lf", "crlf", "mixed" })
                Add(Clean($"{e.Label}/{endings}", e, endings, FixtureKind.Plain, EditFor(e)));

            if (e.Foreign is not null)
                Add(Clean($"{e.Label}/unencodable-edit", e, "lf", FixtureKind.Unencodable, "MARQ" + e.Foreign));

            if (e.InvalidBytes() is { } invalid)
            {
                // The placeholder U+0001 is one byte in every encoding here; it is swapped for the invalid bytes.
                var text = Join(Lines(e, "\u0001"), "lf");
                var clean = e.Strict().GetBytes(text);
                int at = Array.IndexOf(clean, (byte)0x01);
                var bytes = e.Bom.Concat(clean[..at]).Concat(invalid).Concat(clean[(at + 1)..]).ToArray();
                var decoded = e.Replacing().GetString(bytes, e.Bom.Length, bytes.Length - e.Bom.Length);
                Add(new WriteFixture($"{e.Label}/lossy", e, "lf", FixtureKind.Lossy, bytes, decoded, EditFor(e)));
            }
        }

        var latin2 = TherionEncodings.Get("iso8859-2");
        foreach (var kind in new[] { FixtureKind.Symlink, FixtureKind.Mode0640, FixtureKind.ReadOnlyFile, FixtureKind.ReadOnlyFolder, FixtureKind.CaseSibling })
            Add(Clean($"posix/{kind}", latin2, "lf", kind, EditFor(latin2)));

        return all;
    }

    private static WriteFixture Clean(string name, TherionEncodingCase e, string endings, FixtureKind kind, string replace)
    {
        var text = Join(Lines(e), endings);
        var bytes = e.Bom.Concat(e.Strict().GetBytes(text)).ToArray();
        return new WriteFixture(name, e, endings, kind, bytes, text, replace);
    }
}

/// <summary>
/// A fixture on disk in a fresh temporary project: <c>project.thconfig</c> sourcing <c>caves/a.th</c>.
/// Permissions are restored before the folder is deleted.
/// </summary>
internal sealed class PlacedFixture : IDisposable
{
    public string Root { get; }
    public string Folder { get; }
    /// <summary>The path writers are given.</summary>
    public string Path { get; }
    /// <summary>Where the bytes live (the link's target for <see cref="FixtureKind.Symlink"/>).</summary>
    public string ContentPath { get; }
    public string Thconfig { get; }
    public string? SiblingPath { get; }
    public byte[] SiblingBytes { get; } = "survey sibling\nendsurvey\n"u8.ToArray();
    public UnixFileMode? Mode { get; }
    public WriteFixture Fixture { get; }

    private PlacedFixture(WriteFixture fixture)
    {
        Fixture = fixture;
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thide-writepath-" + Guid.NewGuid().ToString("N"));
        Folder = System.IO.Path.Combine(Root, "caves");
        Directory.CreateDirectory(Folder);
        Path = System.IO.Path.Combine(Folder, "a.th");
        Thconfig = System.IO.Path.Combine(Root, "project.thconfig");
        File.WriteAllText(Thconfig, "source caves/a.th\n");
        ContentPath = Path;

        switch (fixture.Kind)
        {
            case FixtureKind.Symlink:
                var store = System.IO.Path.Combine(Root, "store");
                Directory.CreateDirectory(store);
                ContentPath = System.IO.Path.Combine(store, "a.th");
                File.WriteAllBytes(ContentPath, fixture.Bytes);
                File.CreateSymbolicLink(Path, ContentPath);
                break;
            case FixtureKind.CaseSibling:
                File.WriteAllBytes(Path, fixture.Bytes);
                SiblingPath = System.IO.Path.Combine(Folder, "A.th");
                File.WriteAllBytes(SiblingPath, SiblingBytes);
                break;
            default:
                File.WriteAllBytes(Path, fixture.Bytes);
                break;
        }

        if (!OperatingSystem.IsWindows())
        {
            if (fixture.Kind == FixtureKind.Mode0640)
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            if (fixture.Kind == FixtureKind.ReadOnlyFile)
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            if (fixture.Kind == FixtureKind.ReadOnlyFolder)
                File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            Mode = File.GetUnixFileMode(ContentPath);
        }
    }

    /// <summary>
    /// Places <paramref name="fixture"/>, or returns null when this machine cannot express it (POSIX
    /// links and modes on Windows, permissions when running as root, a case sibling on a
    /// case-insensitive file system). The caller returns early on null.
    /// </summary>
    public static PlacedFixture? TryPlace(WriteFixture fixture)
    {
        if (fixture.NeedsPosix && OperatingSystem.IsWindows()) return null;
        if (fixture.Kind is FixtureKind.ReadOnlyFile or FixtureKind.ReadOnlyFolder && !Posix.PermissionsEnforced) return null;
        if (fixture.Kind == FixtureKind.CaseSibling && !Posix.CaseSensitive) return null;
        return new PlacedFixture(fixture);
    }

    public void Dispose()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Folder, (UnixFileMode)0b111_101_101);
                if (File.Exists(ContentPath)) File.SetUnixFileMode(ContentPath, (UnixFileMode)0b110_100_100);
            }
            Directory.Delete(Root, recursive: true);
        }
        catch { /* best-effort cleanup of a temporary folder */ }
    }
}

internal static class Posix
{
    private static readonly Lazy<bool> Enforced = new(() =>
    {
        if (OperatingSystem.IsWindows()) return false;
        var probe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thide-perm-probe-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, "x");
        try
        {
            File.SetUnixFileMode(probe, UnixFileMode.UserRead);
            using (new FileStream(probe, FileMode.Open, FileAccess.Write)) { }
            return false;   // root, or a file system without modes
        }
        catch (UnauthorizedAccessException) { return true; }
        finally
        {
            File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(probe);
        }
    });

    private static readonly Lazy<bool> Sensitive = new(() =>
    {
        var probe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "thide-case-probe-" + Guid.NewGuid().ToString("N") + "a");
        File.WriteAllText(probe, "x");
        try { return !File.Exists(probe[..^1] + "A"); }
        finally { File.Delete(probe); }
    });

    /// <summary>A read-only file refuses a write (false for root or on Windows).</summary>
    public static bool PermissionsEnforced => Enforced.Value;

    /// <summary>The temporary folder's file system tells <c>a</c> from <c>A</c>.</summary>
    public static bool CaseSensitive => Sensitive.Value;
}

internal static class WritePathConformance
{
    /// <summary>
    /// The conformance assertion for one cell. <paramref name="reportedSuccess"/> is what the writer
    /// said happened, or null for a writer that does not say. <paramref name="expectedTransform"/> is a
    /// documented change the writer makes on top of the edit (the editor's save cleanup).
    /// <paramref name="mayDecline"/>: the writer is allowed to leave this file alone (Replace in Files
    /// skips files it takes for binary); it must then leave it byte-identical.
    /// </summary>
    public static void Check(PlacedFixture placed, WriterKind writer, bool? reportedSuccess, string writerName,
        Func<string, string>? expectedTransform = null, bool mayDecline = false)
    {
        var f = placed.Fixture;
        var cell = $"[{writerName} × {f.Name}]";
        var after = File.ReadAllBytes(placed.ContentPath);

        if (f.MustBeRefused(writer) || (mayDecline && after.AsSpan().SequenceEqual(f.Bytes)))
        {
            Assert.True(after.AsSpan().SequenceEqual(f.Bytes),
                $"{cell} the write had to be refused and leave the file byte-identical, but the file changed at byte {FirstDifference(after, f.Bytes)}");
            Assert.True(reportedSuccess != true, $"{cell} the writer reported success for a write it did not make");
        }
        else
        {
            var expectedText = expectedTransform is null ? f.ExpectedText(writer) : expectedTransform(f.ExpectedText(writer));
            var expected = f.Encoding.Bom.Concat(f.Encoding.Strict().GetBytes(expectedText)).ToArray();
            Assert.True(after.AsSpan().SequenceEqual(expected),
                $"{cell} the file is not BOM + the expected text strictly encoded as {f.Encoding.Label}: "
                + (after.AsSpan().SequenceEqual(f.Bytes) ? "it was left unchanged" : $"first difference at byte {FirstDifference(after, expected)}"));
            Assert.True(reportedSuccess != false, $"{cell} the file was written but the writer reported a failure");
            var reread = SourceFileIo.Read(placed.Path);
            Assert.False(reread.Lossy, $"{cell} the written file no longer decodes cleanly");
            Assert.Equal(expectedText, reread.Text);
        }

        var leftovers = Directory.EnumerateFiles(placed.Root, "*.therion-write-*", SearchOption.AllDirectories).ToList();
        Assert.True(leftovers.Count == 0, $"{cell} temporary files were left behind: {string.Join(", ", leftovers)}");

        if (f.Kind == FixtureKind.Symlink)
        {
            Assert.True(new FileInfo(placed.Path).LinkTarget is not null, $"{cell} the symbolic link was replaced by a regular file");
            Assert.Equal(placed.ContentPath, new FileInfo(placed.Path).ResolveLinkTarget(true)?.FullName);
        }
        if (placed.Mode is { } mode)
            Assert.True(mode == File.GetUnixFileMode(placed.ContentPath),
                $"{cell} the file mode changed from {mode} to {File.GetUnixFileMode(placed.ContentPath)}");
        if (placed.SiblingPath is { } sibling)
            Assert.True(File.ReadAllBytes(sibling).AsSpan().SequenceEqual(placed.SiblingBytes),
                $"{cell} the sibling whose name differs only in case was changed");
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return n;
    }
}
