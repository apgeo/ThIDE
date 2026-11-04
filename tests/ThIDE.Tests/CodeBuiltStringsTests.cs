// Strings built in code (status lines, file kinds, template prompts) follow the UI language.
// Inputs are invented; projects go to temp directories.

using System.Globalization;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class CodeBuiltStringsTests
{
    private static readonly CultureInfo Ro = new("ro");

    [Fact]
    public void The_status_bar_file_kind_is_Romanian()
    {
        UiLanguageCollection.In(Ro, () =>
        {
            Assert.Equal("Configurație Therion (.thconfig)", DocumentParser.DescribeType("a.thconfig", SourceFileType.Thconfig));
            Assert.Equal("Text simplu (fără extensie)", DocumentParser.DescribeType("README", SourceFileType.PlainText));
        });
        UiLanguageCollection.In(new CultureInfo("en"), () =>
            Assert.Equal("Therion survey (.th)", DocumentParser.DescribeType("a.th", SourceFileType.Th)));
    }

    [Fact]
    public void Generated_Files_kinds_and_states_are_Romanian_with_the_library_kind_as_fallback()
    {
        UiLanguageCollection.In(Ro, () =>
        {
            var pdf = new ArtifactRow("/tmp/x/out/plan.pdf", "PDF map", 10, DateTimeOffset.UnixEpoch) { IsStale = true };
            Assert.Equal("Hartă PDF", pdf.KindText);
            Assert.Equal("⚠ învechit", pdf.StateText);
            Assert.Equal("actual", (pdf with { IsStale = false }).StateText);
            // A kind with no key (KML reads the same in both languages) shows the library's name.
            Assert.Equal("KML", new ArtifactRow("/tmp/x/out/a.kml", "KML", 1, DateTimeOffset.UnixEpoch).KindText);
        });
    }

    [Fact]
    public void New_Project_prompts_default_title_and_value_errors_are_Romanian()
    {
        var root = Directory.CreateTempSubdirectory("thide-newproj-ro-").FullName;
        try
        {
            UiLanguageCollection.In(Ro, () =>
            {
                var vm = new NewProjectViewModel(Path.Combine(root, "library"), Path.Combine(root, "cave"), () => new DateTime(2026, 10, 28));
                Assert.Equal(["Numele peșterii (fără spații)", "Titlu", "Sistem de coordonate"], vm.Fields.Select(f => f.Prompt));
                Assert.Equal("Peștera mea", vm.Fields.Single(f => f.Variable.Id == "title").Value);

                vm.Fields.Single(f => f.Variable.Id == "project").Value = "two words";
                Assert.Equal("Numele peșterii (fără spații): „two words” nu poate fi scris în proiect", vm.Message);
                vm.Fields.Single(f => f.Variable.Id == "project").Value = "";
                Assert.Equal("Numele peșterii (fără spații): este necesară o valoare", vm.Message);
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_trash_action_is_named_in_Romanian()
    {
        if (OperatingSystem.IsWindows()) return;
        UiLanguageCollection.In(Ro, () =>
            Assert.Contains(new UnixFileOperations().DeleteActionLabel, new[] { "Mută în coșul de gunoi", "Șterge definitiv" }));
    }
}
