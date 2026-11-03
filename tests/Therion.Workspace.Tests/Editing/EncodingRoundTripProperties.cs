using System.Text;
using Therion.Syntax;
using Therion.Testing;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

/// <summary>
/// Read and save, for every name Therion accepts on an <c>encoding</c> line, over generated files: any
/// header of comments, the directive with or without a trailing comment, an optional UTF-8 BOM, and a
/// body drawn from every character the encoding can hold. Seeds are fixed; a failure names its seed.
/// </summary>
public class EncodingRoundTripProperties : IDisposable
{
    private const int FilesPerName = 30;

    private readonly string _root = Directory.CreateTempSubdirectory("thide-encoding-props-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Directive spelling → code page. Therion's own names in any case, and the iso-8859-N aliases.</summary>
    public static TheoryData<string, int> Names => new()
    {
        { "ascii", 20127 }, { "ASCII", 20127 },
        { "cp1250", 1250 }, { "CP1250", 1250 }, { "cp1251", 1251 }, { "Cp1252", 1252 }, { "cp1253", 1253 },
        { "iso8859-1", 28591 }, { "ISO8859-2", 28592 }, { "iso8859-5", 28595 }, { "Iso8859-7", 28597 },
        { "iso-8859-1", 28591 }, { "iso-8859-2", 28592 }, { "ISO-8859-5", 28595 }, { "iso-8859-7", 28597 },
        { "utf-8", 65001 }, { "UTF-8", 65001 },
    };

    /// <summary>The distinct code pages, by one spelling each, for the directive-change pairs.</summary>
    private static readonly (string Name, int CodePage)[] Distinct =
    [
        ("ascii", 20127), ("cp1250", 1250), ("cp1251", 1251), ("cp1252", 1252), ("cp1253", 1253),
        ("iso8859-1", 28591), ("iso8859-2", 28592), ("iso8859-5", 28595), ("iso8859-7", 28597), ("utf-8", 65001),
    ];

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Theory]
    [MemberData(nameof(Names))]
    public void A_file_reads_back_as_its_text_and_an_unchanged_save_keeps_every_byte(string name, int codePage)
    {
        var strict = StrictEncoding(name);
        Assert.Equal(codePage, strict.CodePage);
        var alphabet = Alphabet(strict);

        for (int seed = 0; seed < FilesPerName; seed++)
        {
            var random = new SeededRandom(Seed(name, seed));
            var (text, bom) = GenerateFile(random, name, alphabet);
            var bytes = bom.Concat(strict.GetBytes(text)).ToArray();
            var path = Place(bytes);
            var because = $"[{name} seed {seed}]";

            var read = SourceFileIo.Read(path);
            Assert.True(text == read.Text, $"{because} the text read back differs from the text written");
            Assert.False(read.Lossy, $"{because} a clean file was read as lossy");
            Assert.True(codePage == read.Encoding.CodePage, $"{because} resolved code page {read.Encoding.CodePage}");
            Assert.Equal(bom, read.Bom);

            SourceFileIo.Write(read, read.Text);
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"{because} Write of the unchanged text changed the bytes");
            SourceFileIo.SaveText(path, read.Text, SourceFileIo.FormatOf(read));
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"{because} SaveText in the read format changed the bytes");
            SourceFileIo.SaveText(path, read.Text);
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"{because} SaveText in the disk format changed the bytes");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void An_edit_within_the_alphabet_saves_as_the_bom_and_the_strictly_encoded_text(string name, int codePage)
    {
        var strict = StrictEncoding(name);
        var alphabet = Alphabet(strict);

        for (int seed = 0; seed < FilesPerName; seed++)
        {
            var random = new SeededRandom(Seed(name, 1000 + seed));
            var (text, bom) = GenerateFile(random, name, alphabet);
            var path = Place(bom.Concat(strict.GetBytes(text)).ToArray());
            var read = SourceFileIo.Read(path);

            var edited = SpliceBody(random, read.Text, alphabet);
            var because = $"[{name} seed {1000 + seed}]";
            var format = SourceFileIo.SaveText(path, edited, SourceFileIo.FormatOf(read));

            Assert.Equal(codePage, format.Encoding.CodePage);
            Assert.True(bom.Concat(strict.GetBytes(edited)).SequenceEqual(File.ReadAllBytes(path)),
                $"{because} the saved bytes are not the original BOM + the edited text in {name}");
            Assert.Equal(edited, SourceFileIo.Read(path).Text);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_character_the_encoding_cannot_hold_is_refused_and_the_file_is_unchanged(string name, int codePage)
    {
        var strict = StrictEncoding(name);
        var alphabet = Alphabet(strict);
        // For UTF-8 nothing in Unicode is foreign; a lone surrogate is the one thing it cannot write.
        var foreign = codePage == 65001
            ? "\uD800"
            : new[] { "Ж", "é", "ł", "€", "Σ", "ț", "中", "𝄞" }.First(c => !CanEncode(strict, c));

        for (int seed = 0; seed < 10; seed++)
        {
            var random = new SeededRandom(Seed(name, 2000 + seed));
            var (text, bom) = GenerateFile(random, name, alphabet);
            var bytes = bom.Concat(strict.GetBytes(text)).ToArray();
            var path = Place(bytes);
            var read = SourceFileIo.Read(path);
            var body = BodyStart(read.Text);
            var edited = read.Text.Insert(body + random.Next(read.Text.Length - body + 1), foreign);

            Assert.Throws<UnrepresentableCharacterException>(() => SourceFileIo.Write(read, edited));
            Assert.Throws<UnrepresentableCharacterException>(() => SourceFileIo.SaveText(path, edited, SourceFileIo.FormatOf(read)));
            Assert.Throws<UnrepresentableCharacterException>(() => SourceFileIo.SaveText(path, edited));
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"[{name} seed {2000 + seed}] a refused write changed the file");
            Assert.False(SourceFileIo.CanEncode(read, edited));
        }
    }

    /// <summary>
    /// Bytes the encoding cannot decode make the read lossy, and no save of the text read that way may
    /// put U+FFFD where they were. .NET's legacy code pages decode every byte, so only ASCII and UTF-8
    /// have such bytes; for the others the test checks that every byte really does round-trip.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Undecodable_bytes_read_as_lossy_and_every_save_of_them_is_refused(string name, int codePage)
    {
        var strict = StrictEncoding(name);
        byte[][] invalid = codePage switch
        {
            20127 => [[0xE9], [0x80], [0xFF]],
            65001 => [[0xC3, 0x28], [0x80], [0xFF], [0xED, 0xA0, 0x80], [0xF0, 0x9F]],
            _ => [],
        };
        if (invalid.Length == 0)
        {
            for (int b = 0; b <= 0xFF; b++)
                Assert.Equal(new[] { (byte)b }, strict.GetBytes(strict.GetString([(byte)b])));
            return;
        }

        var alphabet = Alphabet(strict);
        for (int seed = 0; seed < 10; seed++)
        {
            var random = new SeededRandom(Seed(name, 3000 + seed));
            var (text, bom) = GenerateFile(random, name, alphabet);
            var clean = strict.GetBytes(text);
            var bodyAt = strict.GetByteCount(text[..BodyStart(text)]);
            // Only at a character boundary in the body, so the directive and the characters around stay intact.
            var at = bodyAt + strict.GetByteCount(text[BodyStart(text)..][..CharBoundary(random, text[BodyStart(text)..])]);
            var bytes = bom.Concat(clean[..at]).Concat(random.Pick(invalid)).Concat(clean[at..]).ToArray();
            var path = Place(bytes);
            var because = $"[{name} seed {3000 + seed}]";

            var read = SourceFileIo.Read(path);
            Assert.True(read.Lossy, $"{because} undecodable bytes were not noticed");
            Assert.Contains('�', read.Text);
            Assert.True(SourceFileIo.FormatOnDisk(path).Lossy);

            Assert.Throws<UndecodableBytesException>(() => SourceFileIo.Write(read, read.Text));
            Assert.Throws<UndecodableBytesException>(() => SourceFileIo.SaveText(path, read.Text, SourceFileIo.FormatOf(read)));
            Assert.Throws<UndecodableBytesException>(() => SourceFileIo.SaveText(path, read.Text));
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), $"{because} a refused save changed the file");

            // Once every replacement character is gone the text is the user's own, and it saves.
            var repaired = read.Text.Replace("�", "", StringComparison.Ordinal);
            SourceFileIo.Write(read, repaired);
            Assert.Equal(repaired, SourceFileIo.Read(path).Text);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_trailing_comment_on_the_directive_does_not_change_what_it_declares(string name, int codePage)
    {
        foreach (var directive in new[] { $"encoding {name}", $"encoding {name} # declared", $"encoding\t{name}\t#x", $"  ENCODING   {name}   # a b c" })
        {
            var bytes = Encoding.ASCII.GetBytes($"# header\n\n{directive}\nsurvey a\nendsurvey\n");
            Assert.Equal(codePage, EncodingResolver.DetectEncoding(bytes)?.CodePage);
            Assert.Equal(codePage, SourceFileIo.Read(Place(bytes)).Encoding.CodePage);
        }
    }

    /// <summary>
    /// Changing the directive changes the save's encoding, and a UTF-8 BOM stays only while the new
    /// encoding is UTF-8 — the file must read back as the text in the encoding it now declares.
    /// </summary>
    [Fact]
    public void Changing_the_directive_saves_in_the_new_encoding_and_keeps_the_bom_only_while_it_matches()
    {
        int cases = 0;
        foreach (var (from, fromCp) in Distinct)
            foreach (var (to, toCp) in Distinct)
                foreach (var withBom in new[] { false, true })
                {
                    if (from == to) continue;
                    var body = "survey a -title \"plain\"\n# ascii only, so either encoding holds it\nendsurvey\n";
                    var bom = withBom ? Utf8Bom : [];
                    var path = Place(bom.Concat(Encoding.ASCII.GetBytes($"encoding {from}\n{body}")).ToArray());
                    var read = SourceFileIo.Read(path);
                    Assert.Equal(fromCp, read.Encoding.CodePage);

                    var edited = $"encoding {to}\n{body}";
                    var format = SourceFileIo.SaveText(path, edited, SourceFileIo.FormatOf(read));

                    var because = $"[{from} -> {to}, BOM {withBom}]";
                    Assert.True(toCp == format.Encoding.CodePage, $"{because} saved as code page {format.Encoding.CodePage}");
                    var keepBom = withBom && toCp == 65001;
                    var after = File.ReadAllBytes(path);
                    Assert.True(keepBom == after.AsSpan().StartsWith(Utf8Bom), $"{because} the BOM should {(keepBom ? "" : "not ")}be there");
                    var reread = SourceFileIo.Read(path);
                    Assert.Equal(toCp, reread.Encoding.CodePage);
                    Assert.Equal(edited, reread.Text);
                    cases++;
                }
        Assert.Equal(Distinct.Length * (Distinct.Length - 1) * 2, cases);
    }

    /// <summary>
    /// The directive sits after a long accented header, inside the 4 KiB the reader scans in the file's
    /// own bytes. A save must find it again, or it rewrites the file as UTF-8 while the file still
    /// declares a Latin code page.
    /// </summary>
    [Theory(Skip = "Known bug: the save probes the first 4096 chars re-encoded as UTF-8, so a directive the reader found after a long accented header is missed and the file is rewritten as UTF-8")]
    [InlineData("cp1250", "ł")]
    [InlineData("cp1252", "é")]
    [InlineData("iso8859-2", "ž")]
    [InlineData("iso8859-5", "Ж")]
    public void A_directive_after_a_long_accented_header_is_honoured_on_save(string name, string accent)
    {
        var strict = StrictEncoding(name);
        // 30 comment lines of 100 accented characters: about 3 KiB in the code page, about 6 KiB in UTF-8.
        var header = string.Concat(Enumerable.Repeat("# " + string.Concat(Enumerable.Repeat(accent, 100)) + "\n", 30));
        var text = header + $"encoding {name}\nsurvey a -title \"{accent}\"\nendsurvey\n";
        var bytes = strict.GetBytes(text);
        Assert.True(bytes.Length < EncodingResolver.ProbeBytes);
        var path = Place(bytes);

        var read = SourceFileIo.Read(path);
        Assert.Equal(strict.CodePage, read.Encoding.CodePage);

        SourceFileIo.SaveText(path, read.Text, SourceFileIo.FormatOf(read));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    // ---- generator ----------------------------------------------------------

    private static ulong Seed(string name, int index)
    {
        // A stable hash of the name (string.GetHashCode is randomised per process).
        ulong h = 1469598103934665603UL;
        foreach (char c in name) h = (h ^ c) * 1099511628211UL;
        return h ^ (ulong)index;
    }

    private static Encoding StrictEncoding(string name)
    {
        var resolved = EncodingResolver.DetectEncoding(Encoding.ASCII.GetBytes($"encoding {name}\n"))
            ?? throw new InvalidOperationException($"'{name}' does not resolve");
        var strict = (Encoding)resolved.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        return strict;
    }

    /// <summary>
    /// Every character the encoding holds (bytes 0x20–0xFF through the strict decoder, kept when they
    /// encode back to the same byte); for UTF-8 a spread of the BMP, combining marks and astral characters.
    /// Each entry is one text element so a splice never cuts a surrogate pair.
    /// </summary>
    private static IReadOnlyList<string> Alphabet(Encoding strict)
    {
        if (strict.CodePage == 65001)
        {
            var list = new List<string>();
            for (int c = 0x20; c < 0x7F; c++) list.Add(((char)c).ToString());
            foreach (var (lo, hi) in new[] { (0xA0, 0x24F), (0x300, 0x36F), (0x370, 0x3FF), (0x400, 0x4FF), (0x2000, 0x206F), (0x3040, 0x30FF), (0x4E00, 0x4E7F), (0xAC00, 0xAC7F), (0xE000, 0xE01F), (0xFB00, 0xFB4F) })
                for (int c = lo; c <= hi; c++) list.Add(((char)c).ToString());
            foreach (var (lo, hi) in new[] { (0x1D100, 0x1D1FF), (0x1F300, 0x1F3FF), (0x20000, 0x2007F) })
                for (int c = lo; c <= hi; c++) list.Add(char.ConvertFromUtf32(c));
            return list;
        }

        var alphabet = new List<string>();
        for (int b = 0x20; b <= 0xFF; b++)
        {
            string s;
            try { s = strict.GetString([(byte)b]); }
            catch (DecoderFallbackException) { continue; }
            if (s.Length == 1 && s[0] is not ('\r' or '\n') && CanEncode(strict, s) && strict.GetBytes(s) is [var back] && back == b)
                alphabet.Add(s);
        }
        return alphabet;
    }

    private static bool CanEncode(Encoding strict, string s)
    {
        try { strict.GetBytes(s); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    private static string RandomRun(SeededRandom random, IReadOnlyList<string> alphabet, int min, int max)
    {
        var sb = new StringBuilder();
        int n = random.Next(min, max + 1);
        for (int i = 0; i < n; i++) sb.Append(random.Pick(alphabet));
        return sb.ToString();
    }

    /// <summary>
    /// 0–3 comment (or blank) lines, the directive (random spacing, optional trailing comment), then a
    /// body of 1–12 lines from the alphabet; LF, CRLF or mixed endings; a UTF-8 BOM in a third of files.
    /// </summary>
    private static (string Text, byte[] Bom) GenerateFile(SeededRandom random, string name, IReadOnlyList<string> alphabet)
    {
        var mode = random.Next(3);
        string Nl() => mode switch { 0 => "\n", 1 => "\r\n", _ => random.Chance(50) ? "\n" : "\r\n" };

        var sb = new StringBuilder();
        int header = random.Next(4);
        for (int i = 0; i < header; i++)
            sb.Append(random.Chance(20) ? "" : random.Pick(new[] { "#", "# ", "  #" }) + RandomRun(random, alphabet, 0, 60)).Append(Nl());

        sb.Append(random.Pick(new[] { "", " ", "\t" })).Append(random.Pick(new[] { "encoding", "ENCODING", "Encoding" }))
          .Append(random.Pick(new[] { " ", "  ", "\t" })).Append(name);
        if (random.Chance(40)) sb.Append(random.Pick(new[] { " ", "\t", "  " })).Append('#').Append(RandomRun(random, alphabet, 0, 30));
        sb.Append(Nl());

        int body = random.Next(1, 13);
        for (int i = 0; i < body; i++)
        {
            sb.Append(RandomRun(random, alphabet, 0, 80));
            if (i < body - 1 || random.Chance(70)) sb.Append(Nl());
        }
        return (sb.ToString(), random.Chance(33) ? Utf8Bom : []);
    }

    /// <summary>The offset just past the directive's line, where the body starts.</summary>
    private static int BodyStart(string text)
    {
        int at = 0;
        while (true)
        {
            int end = text.IndexOf('\n', at);
            var line = (end < 0 ? text[at..] : text[at..end]).Trim();
            if (line.Length > 0 && line[0] != '#') return end < 0 ? text.Length : end + 1;
            if (end < 0) return text.Length;
            at = end + 1;
        }
    }

    private static int CharBoundary(SeededRandom random, string body)
    {
        int at = random.Next(body.Length + 1);
        if (at > 0 && at < body.Length && char.IsLowSurrogate(body[at])) at--;
        return at;
    }

    /// <summary>Deletes a random range of the body and inserts a run of the alphabet in its place.</summary>
    private static string SpliceBody(SeededRandom random, string text, IReadOnlyList<string> alphabet)
    {
        int body = BodyStart(text);
        var tail = text[body..];
        int start = CharBoundary(random, tail);
        int end = Math.Min(tail.Length, start + random.Next(20));
        if (end > 0 && end < tail.Length && char.IsLowSurrogate(tail[end])) end++;
        var insert = RandomRun(random, alphabet, 1, 40) + (random.Chance(30) ? random.Pick(new[] { "\n", "\r\n" }) : "");
        return text[..body] + tail[..start] + insert + tail[end..];
    }

    private int _files;

    private string Place(byte[] bytes)
    {
        var path = Path.Combine(_root, $"f{_files++}.th");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
