using Therion.Testing;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

/// <summary>
/// The library's write primitives against the shared fixture matrix: every Therion encoding, every
/// line-ending style, lossy files, unencodable edits, links, modes, read-only files and folders, and a
/// case-variant sibling. Each cell is written exactly or refused with the file untouched.
/// </summary>
public class WritePathConformanceTests
{
    public static TheoryData<string> AllFixtures => WriteFixtures.Names();

    /// <summary>The fixtures a newly created file can carry (no POSIX extras, no lossy bytes).</summary>
    public static TheoryData<string> CreatableFixtures =>
        WriteFixtures.Names(f => f.Kind is FixtureKind.Plain or FixtureKind.Unencodable);

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Write_over_a_read_file_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        bool reported = Attempt(() =>
        {
            var file = SourceFileIo.Read(placed.Path);
            SourceFileIo.Write(file, file.Text.Replace(WriteFixture.Find, placed.Fixture.Replace, StringComparison.Ordinal));
        });

        WritePathConformance.Check(placed, WriterKind.TextEdit, reported, "SourceFileIo.Write");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Save_text_in_the_format_it_was_read_in_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        bool reported = Attempt(() =>
        {
            var file = SourceFileIo.Read(placed.Path);
            SourceFileIo.SaveText(placed.Path, file.Text.Replace(WriteFixture.Find, placed.Fixture.Replace, StringComparison.Ordinal),
                SourceFileIo.FormatOf(file));
        });

        WritePathConformance.Check(placed, WriterKind.TextEdit, reported, "SourceFileIo.SaveText(read format)");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Save_text_in_the_format_on_disk_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        bool reported = Attempt(() =>
        {
            var text = SourceFileIo.Read(placed.Path).Text;
            SourceFileIo.SaveText(placed.Path, text.Replace(WriteFixture.Find, placed.Fixture.Replace, StringComparison.Ordinal));
        });

        WritePathConformance.Check(placed, WriterKind.TextEdit, reported, "SourceFileIo.SaveText(disk format)");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Reindent_written_back_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        bool reported = Attempt(() =>
        {
            var file = SourceFileIo.Read(placed.Path);
            SourceFileIo.Write(file, global::Therion.Workspace.Formatting.TherionReindenter.Format(file.Text, "  "));
        });

        WritePathConformance.Check(placed, WriterKind.Reindent, reported, "reindent + SourceFileIo.Write");
    }

    /// <summary>
    /// A new file is written in the encoding its own <c>encoding</c> line declares, else UTF-8, never with
    /// a BOM; content its encoding cannot hold creates nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(CreatableFixtures))]
    public void Create_writes_the_declared_encoding_or_nothing(string fixture)
    {
        var f = WriteFixtures.Get(fixture);
        var root = Directory.CreateTempSubdirectory("thide-create-").FullName;
        try
        {
            var path = Path.Combine(root, "deep", "er", "new.th");
            var content = f.Kind == FixtureKind.Unencodable ? f.EditedText : f.Text;
            bool reported = Attempt(() => SourceFileIo.Create(path, content));

            if (f.Kind == FixtureKind.Unencodable)
            {
                Assert.False(reported, $"[{fixture}] content its encoding cannot hold was created");
                Assert.False(File.Exists(path));
                Assert.False(Directory.Exists(Path.Combine(root, "deep")), $"[{fixture}] a refused create left its folders behind");
                return;
            }

            Assert.True(reported, $"[{fixture}] the create failed");
            var declared = f.Encoding.Directive is null ? TherionEncodings.Get("utf-8-plain") : f.Encoding;
            Assert.Equal(declared.Strict().GetBytes(content), File.ReadAllBytes(path));
            Assert.Equal(content, SourceFileIo.Read(path).Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_matrix_covers_every_encoding_line_ending_and_hazard()
    {
        var all = WriteFixtures.All;
        Assert.Equal(TherionEncodings.All.Count * 3, all.Count(f => f.Kind == FixtureKind.Plain));
        Assert.Equal(TherionEncodings.All.Count(e => e.Foreign is not null), all.Count(f => f.Kind == FixtureKind.Unencodable));
        // ASCII and UTF-8 (with and without a directive or BOM) have bytes they cannot decode. .NET's legacy
        // code pages map every byte to some character (undefined ones included), so they have no lossy cell.
        foreach (var label in new[] { "ascii", "utf-8", "utf-8-plain", "utf-8-bom" })
            Assert.True(all.Any(f => f.Name == label + "/lossy"),
                $"no lossy cell for {label}; there are: {string.Join(", ", all.Where(f => f.Kind == FixtureKind.Lossy).Select(f => f.Name))}");
        Assert.Equal(5, all.Count(f => f.NeedsPosix));

        foreach (var f in all)
        {
            Assert.Equal(1, CountOf(f.Text, WriteFixture.Find));
            Assert.Equal(f.Kind == FixtureKind.Lossy, f.Text.Contains('�'));
            Assert.NotEqual(f.Text, f.ReindentedText);   // a re-indenting writer always has something to write
        }
    }

    private static int CountOf(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>True when <paramref name="write"/> returned, false when it refused with one of the documented exceptions.</summary>
    private static bool Attempt(Action write)
    {
        try { write(); return true; }
        catch (Exception ex) when (ex is UnrepresentableCharacterException or UnauthorizedAccessException or IOException) { return false; }
    }
}
