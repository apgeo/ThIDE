using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Syntax;
using Therion.Testing;
using Therion.Workspace.Editing;
using Therion.Workspace.Fixes;
using Therion.Workspace.Formatting;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

/// <summary>
/// The app's write paths (editor save, the batch edit service behind Link survey / Renumber / Fix all,
/// Format all files, Replace in Files) against the shared fixture matrix: every Therion encoding and
/// line-ending style, lossy files, unencodable edits, links, modes, read-only files and folders, a
/// case-variant sibling. Each cell is written exactly or refused with the file untouched.
/// </summary>
public class WritePathConformanceTests
{
    public static TheoryData<string> AllFixtures => WriteFixtures.Names();

    /// <summary>Format all with Keep: the mixed-ending cells have their own (known-bug) test.</summary>
    public static TheoryData<string> UniformEndingFixtures => WriteFixtures.Names(f => f.Endings != "mixed");

    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(ImmutableArray<string>.Empty, pathOrFolder, ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewDocuments() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    private static WorkspaceEditService NewEditService()
    {
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name == "get_Documents" ? new ObservableCollection<FileDocumentViewModel>() : null);
        var session = WorkspaceEditServiceTests.Stub<IWorkspaceSession>.Create(m => m.Name == "SetActiveThconfigAsync" ? Task.FromResult(true) : null);
        return new WorkspaceEditService(docs, session);
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Editor_save_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;
        var svc = NewDocuments();
        await svc.OpenFileAsync(placed.Path);
        var doc = svc.Documents.Single();
        doc.DocumentText = doc.DocumentText.Replace(WriteFixture.Find, placed.Fixture.Replace, StringComparison.Ordinal);

        // A failed write is reported and rethrown; an encoding refusal returns false.
        bool saved;
        try { saved = await svc.SaveDocumentAsync(doc); }
        catch (UnauthorizedAccessException) { saved = false; }

        // The save's documented cleanup (trailing blanks, final newline) comes on top of the edit.
        WritePathConformance.Check(placed, WriterKind.TextEdit, saved, "editor save", t => EditorTextCleanup.ApplyOnSave(t, null));
        Assert.Equal(!saved, doc.IsDirty);   // a refused save keeps the edit as unsaved, a done one clears it
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public async Task Batch_edit_service_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;
        var text = SourceFileIo.Read(placed.Path).Text;
        var at = text.IndexOf(WriteFixture.Find, StringComparison.Ordinal);
        var batch = new EditBatch([new FileTextEdits(placed.Path, [new TextEdit(at, WriteFixture.Find.Length, WriteFixture.Find, placed.Fixture.Replace)])], [], []);

        var outcome = await NewEditService().ApplyAsync(batch);

        WritePathConformance.Check(placed, WriterKind.TextEdit, outcome.Success, "WorkspaceEditService");
    }

    [Theory]
    [MemberData(nameof(UniformEndingFixtures))]
    public async Task Format_all_files_conforms(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        var outcome = await FormatAllAsync(placed);

        WritePathConformance.Check(placed, WriterKind.Reindent, outcome, "Format all files");
    }

    [Theory(Skip = "Known bug: Format all with Keep rewrites every line of a file that mixes CRLF and LF with CRLF")]
    [MemberData(nameof(AllFixtures))]
    public async Task Format_all_files_conforms_on_mixed_line_endings(string fixture)
    {
        if (WriteFixtures.Get(fixture).Endings != "mixed") return;
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;

        var outcome = await FormatAllAsync(placed);

        WritePathConformance.Check(placed, WriterKind.Reindent, outcome, "Format all files");
    }

    /// <summary>As Tools ▸ Format all files: plan with Keep from the text the app reads, then apply through the edit service.</summary>
    private static async Task<bool?> FormatAllAsync(PlacedFixture placed)
    {
        var (batch, _, skipped) = FormatAll.Plan([placed.Path], p => File.Exists(p) ? EncodingResolver.ReadAllText(p) : null,
            LineEndings.Keep, t => TherionReindenter.Format(t, "  "), n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Empty(skipped);
        if (batch.IsEmpty) return false;
        return (await NewEditService().ApplyAsync(batch)).Success;
    }

    /// <summary>Replace in Files: the ASCII-declared file with an 8-bit byte has its own (known-bug) test.</summary>
    public static TheoryData<string> ReplaceInFilesFixtures => WriteFixtures.Names(f => f.Name != "ascii/lossy");

    [Theory]
    [MemberData(nameof(ReplaceInFilesFixtures))]
    public async Task Replace_in_files_conforms(string fixture) => await ReplaceInFilesAsync(fixture);

    /// <summary>
    /// A file declared ASCII that holds an 8-bit byte reads as lossy; Replace All must leave it alone
    /// rather than write '?' where the byte was.
    /// </summary>
    [Fact(Skip = "Known bug: Replace in Files decodes with EncodingResolver.Decode, whose ASCII fallback yields '?' rather than U+FFFD, so its lossy check misses the file and Replace All writes '?' over the 8-bit byte")]
    public async Task Replace_in_files_leaves_an_ascii_file_with_an_8bit_byte_alone() => await ReplaceInFilesAsync("ascii/lossy");

    private static async Task ReplaceInFilesAsync(string fixture)
    {
        using var placed = PlacedFixture.TryPlace(WriteFixtures.Get(fixture));
        if (placed is null) return;
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name == "get_Documents" ? new ObservableCollection<FileDocumentViewModel>() : null);
        var vm = new ReplaceInFilesViewModel(docs)
        {
            Scope = ReplaceInFilesViewModel.ScopeDirectory,
            Directory = placed.Folder,
            FileMask = "*.th",
            Query = WriteFixture.Find,
            Replacement = placed.Fixture.Replace,
            MatchCase = true,
        };

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        // UTF-16 holds NUL bytes, so Replace in Files takes it for binary and skips it (Therion reads no UTF-16).
        WritePathConformance.Check(placed, WriterKind.TextEdit, null, "Replace in Files",
            mayDecline: placed.Fixture.Encoding.CodePage is 1200 or 1201);
    }

    /// <summary>
    /// The reader finds the directive after a long accented header (it scans the file's own bytes); the
    /// editor's save must keep the file in that encoding.
    /// </summary>
    [Fact(Skip = "Known bug: the save probes the first 4096 chars re-encoded as UTF-8, so a directive the reader found after a long accented header is missed and the file is rewritten as UTF-8")]
    public async Task Editor_save_keeps_the_encoding_a_directive_after_a_long_accented_header_declares()
    {
        using var dir = new TempDir();
        var latin2 = TherionEncodings.Get("iso8859-2").Strict();
        var header = string.Concat(Enumerable.Repeat("# " + new string('ž', 100) + "\n", 30));
        var path = Path.Combine(dir.Path, "a.th");
        File.WriteAllBytes(path, latin2.GetBytes(header + "encoding iso8859-2\nsurvey a\n# MARK\nendsurvey\n"));
        var svc = NewDocuments();
        await svc.OpenFileAsync(path);
        var doc = svc.Documents.Single();
        doc.DocumentText = doc.DocumentText.Replace("MARK", "MARQ ž", StringComparison.Ordinal);

        Assert.True(await svc.SaveDocumentAsync(doc));

        Assert.Equal(latin2.GetBytes(header + "encoding iso8859-2\nsurvey a\n# MARQ ž\nendsurvey\n"), File.ReadAllBytes(path));
    }
}
