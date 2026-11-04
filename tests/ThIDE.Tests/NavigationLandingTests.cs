// Where navigation leaves the user: the status bar follows the tab that is shown, an Outline click
// reveals a line without taking focus from the tree, and Find All References with nothing to list
// says so on the status bar.

using System.IO;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using ThIDE.Services;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class NavigationLandingTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "thide-invented");
    private static readonly string FileA = Path.Combine(Root, "a.th");
    private static readonly string FileB = Path.Combine(Root, "b.th");

    private static SourceSpan Caret(string file, int line, int column, int offset) =>
        new(file, new SourceLocation(line, column), new SourceLocation(line, column), offset, 0);

    [Fact]
    public void Tab_activation_shows_that_files_caret_on_the_status_bar() => UiThread.Run(() =>
    {
        var docs = new ScriptedDocumentService();
        var vm = MainWindowViewModelHost.Create(docs);
        var a = docs.OpenTextDocument(FileA, "survey a\n  centreline\n  endcentreline\nendsurvey\n");
        a.SetCaret(Caret(FileA, 3, 5, 27));
        var b = docs.OpenTextDocument(FileB, "survey b\nendsurvey\n");

        // b has never reported a caret: it starts at 1:1, not at a's 3:5.
        Assert.Equal((1, 1, 0), (vm.StatusCaretLine, vm.StatusCaretCol, vm.StatusCaretPos));

        docs.SetActive(a);
        Assert.Equal((3, 5, 27), (vm.StatusCaretLine, vm.StatusCaretCol, vm.StatusCaretPos));

        docs.SetActive(b);
        Assert.Equal((1, 1, 0), (vm.StatusCaretLine, vm.StatusCaretCol, vm.StatusCaretPos));
    });

    [Fact]
    public void Outline_click_reveals_the_line_without_focusing_the_editor() => UiThread.Run(() =>
    {
        var docs = new ScriptedDocumentService();
        var doc = docs.OpenTextDocument(FileA, "survey a\n  centreline\n  endcentreline\nendsurvey\n");
        var outline = new OutlineViewModel(docs);
        var centreline = outline.Roots.Single().Children.Single();

        outline.RevealNode(centreline);

        Assert.Equal(2, doc.PendingScroll?.Start.Line);
        Assert.False(doc.PendingScrollFocus);
    });

    [Fact]
    public void A_navigation_scroll_focuses_the_editor_by_default()
    {
        var doc = new ThIDE.ViewModels.Docking.FileDocumentViewModel(FileA, "survey a\nendsurvey\n", new MeasurementsViewModel());
        doc.RequestScrollTo(Caret(FileA, 1, 1, 0) with { Length = 1 }, focus: false);
        doc.RequestScrollTo(Caret(FileA, 2, 1, 9) with { Length = 1 });
        Assert.True(doc.PendingScrollFocus);
    }

    [Fact]
    public void No_references_at_all_is_reported_on_the_status_bar() => UiThread.Run(() =>
    {
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService());

        Assert.True(vm.ReportIfNoOtherReferences("nowhere", Array.Empty<SymbolReference>()));
        Assert.Contains("nowhere", vm.StatusText);
    });

    [Fact]
    public void A_declaration_with_no_uses_reports_no_other_references() => UiThread.Run(() =>
    {
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService());
        var declaration = new SymbolReference(Caret(FileA, 4, 3, 40) with { Length = 2 }, IsDeclaration: true);

        Assert.True(vm.ReportIfNoOtherReferences("st1", new[] { declaration }));
        Assert.Contains("st1", vm.StatusText);
        Assert.NotEqual(string.Format(Resources.Tr.Get("Refs_None"), "st1"), vm.StatusText);
    });

    [Fact]
    public void References_beyond_the_declaration_are_left_to_the_list() => UiThread.Run(() =>
    {
        var vm = MainWindowViewModelHost.Create(new ScriptedDocumentService());
        vm.StatusText = "unchanged";
        var refs = new[]
        {
            new SymbolReference(Caret(FileA, 4, 3, 40) with { Length = 2 }, IsDeclaration: true),
            new SymbolReference(Caret(FileB, 9, 1, 80) with { Length = 2 }, IsDeclaration: false),
        };

        Assert.False(vm.ReportIfNoOtherReferences("st1", refs));
        Assert.Equal("unchanged", vm.StatusText);
    });

    [Fact]
    public void Shift_F12_lists_references_and_next_occurrence_has_its_own_gesture()
    {
        var shortcuts = new JsonKeyboardShortcutService(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "keyboard.json"));
        Assert.Equal("Shift+F12", shortcuts.Defaults[ShellCommandIds.FindReferences]);
        Assert.Equal("Ctrl+Shift+F12", shortcuts.Defaults[ShellCommandIds.NextOccurrenceInFile]);
    }
}
