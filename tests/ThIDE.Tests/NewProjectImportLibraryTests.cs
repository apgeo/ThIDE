using ThIDE.ViewModels;

namespace ThIDE.Tests;

/// <summary>
/// Import is called from async void window handlers with no catch, so an exception thrown while
/// preparing the template library would take the app down. It must report the problem instead.
/// </summary>
public class NewProjectImportLibraryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thide-newproj-lib-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Import_reports_an_uncreatable_library_instead_of_throwing()
    {
        // A file occupies the library's path, so the library folder cannot be created.
        var library = Path.Combine(_root, "library");
        File.WriteAllText(library, "not a folder");

        var src = Path.Combine(_root, "club");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "template.json"), """{ "id": "club", "title": "Club layout" }""");
        File.WriteAllText(Path.Combine(src, "main.th"), "survey main\nendsurvey\n");

        var vm = new NewProjectViewModel(library, Path.Combine(_root, "cave"), () => new DateTime(2026, 10, 28));

        var ex = Record.Exception(() => vm.Import(src));

        Assert.True(ex is null, $"Import threw instead of returning a message: {ex?.GetType().Name}: {ex?.Message}");
    }
}
