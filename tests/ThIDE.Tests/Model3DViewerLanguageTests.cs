// The embedded 3D viewer (CaveView) reads its interface language, the HUD's "height" caption among it,
// when its page starts: the page is opened in the UI language (Tr.Culture, not the ambient culture)
// and loaded again when the language is switched while it is open.

using System.Globalization;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class Model3DViewerLanguageTests
{
    private static readonly CultureInfo Ro = new("ro-RO");

    private static void English(Action body) => UiLanguageCollection.In(CultureInfo.InvariantCulture, () => UiThread.Run(body));

    private sealed class FakeHost : ICaveview3DAssetHost
    {
        public bool IsAvailable => true;
        public bool IsListening => true;
        public bool TryStart() => true;
        public string? ViewerUrl => "http://127.0.0.1:1/viewer.html";
        public string? StageModel(string modelPath) => "m1.lox";
        public void Dispose() { }
    }

    private sealed class FakeShell : Therion.Build.IShellOpener
    {
        public bool Open(string path) => true;
        public bool RevealInFileManager(string path) => true;
    }

    [Fact]
    public void The_3D_viewer_page_takes_the_UI_language_and_reloads_on_a_switch() => English(() =>
    {
        var ambient = CultureInfo.CurrentUICulture;
        try
        {
            // The ambient culture can still name the start-up language; the page follows Tr.Culture.
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Tr.Culture = Ro;
            var vm = new Model3DViewerViewModel(new FakeHost(), new StationSourceResolver(), new NullDocumentService(), new FakeShell());
            Assert.Equal("http://127.0.0.1:1/viewer.html?lang=ro", vm.EnsureStarted());

            var navigations = new List<string>();
            vm.NavigateRequested += (_, url) => navigations.Add(url);
            vm.OnUiLanguageChanged();
            Assert.Empty(navigations);   // same language: the page stays

            Tr.Culture = CultureInfo.InvariantCulture;
            vm.OnUiLanguageChanged();
            Assert.Equal(new[] { "http://127.0.0.1:1/viewer.html" }, navigations);

            Tr.Culture = Ro;
            vm.OnUiLanguageChanged();
            Assert.Equal("http://127.0.0.1:1/viewer.html?lang=ro", navigations[^1]);
        }
        finally { CultureInfo.CurrentUICulture = ambient; }
    });

    [Fact]
    public void The_3D_viewer_does_not_reload_a_page_it_has_not_opened() => English(() =>
    {
        var vm = new Model3DViewerViewModel(new FakeHost(), new StationSourceResolver(), new NullDocumentService(), new FakeShell());
        var navigations = 0;
        vm.NavigateRequested += (_, _) => navigations++;
        Tr.Culture = Ro;
        vm.OnUiLanguageChanged();
        Assert.Equal(0, navigations);
    });
}
