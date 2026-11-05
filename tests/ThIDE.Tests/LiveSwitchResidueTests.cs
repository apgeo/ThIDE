// What a live switch of the UI language left behind: the toolbar compile status kept the language of
// the last compilation, the time-mode combo kept the text it was first drawn with, and a document went
// back to its Source view. Numbers in display text use the UI language's decimal separator, like the
// compile timings already did. All inputs are invented.

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Therion.Build;
using Therion.Build.Automation;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class LiveSwitchResidueTests
{
    private static readonly CultureInfo Ro = new("ro-RO");

    private sealed class FixedCompiler(int exitCode, params string[] artifacts) : ITherionCompiler
    {
        public ValueTask<CompileResult> CompileAsync(string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
            => new(new CompileResult(exitCode, ImmutableArray<Diagnostic>.Empty,
                artifacts.Select(a => new OutputArtifact(a, "lox", 1, DateTimeOffset.UtcNow)).ToImmutableArray()));
    }

    private sealed class NoShell : IShellOpener
    {
        public bool Open(string path) => true;
        public bool RevealInFileManager(string path) => true;
    }

    private sealed class NoTools : IExternalToolLocator
    {
        public ValueTask<ToolInfo?> FindAsync(string toolId, CancellationToken cancellationToken = default) => new((ToolInfo?)null);
    }

    private sealed class NoCache : IOutputArtifactCache
    {
        public ImmutableArray<OutputArtifact> Load(string entryPointPath, string therionVersion) => ImmutableArray<OutputArtifact>.Empty;
        public void Save(string entryPointPath, string therionVersion, ImmutableArray<OutputArtifact> artifacts) { }
        public void Clear(string entryPointPath, string therionVersion) { }
    }

    private static BuildViewModel Build(ITherionCompiler compiler)
    {
        var docs = WorkspaceEditServiceTests.Stub<IDocumentService>.Create(m => m.Name switch
        {
            "get_Documents" => new ObservableCollection<FileDocumentViewModel>(),
            "get_CurrentDiagnostics" => ImmutableArray<Diagnostic>.Empty,
            _ => null,
        });
        return new BuildViewModel(compiler, new CompileGate(), new NoShell(), new NoTools(), docs, new NoCache());
    }

    private static Task English(Func<Task> body) =>
        UiThread.RunAsync(async () =>
        {
            var saved = UiLanguageCollection.Pinned;
            Tr.Culture = CultureInfo.InvariantCulture;
            try { await body(); }
            finally { UiLanguageCollection.Pinned = saved; }
        });

    [Fact]
    public Task The_compile_status_and_its_artifact_link_follow_a_language_switch() => English(async () =>
    {
        using var dir = new TempDir();
        var cfg = dir.Write("demo.thconfig", "source demo.th\n");
        var vm = Build(new FixedCompiler(0, Path.Combine(dir.Path, "a.lox"), Path.Combine(dir.Path, "b.lox")));
        await vm.RunBuildAsync(cfg);
        Assert.Equal("Compilation succeeded", vm.Status);
        Assert.Equal("2 artifacts", vm.StatusArtifactCount);

        Tr.Culture = Ro;
        vm.Relocalize();
        Assert.Equal("Compilare reușită", vm.Status);
        Assert.Equal("2 fișiere generate", vm.StatusArtifactCount);

        Tr.Culture = CultureInfo.InvariantCulture;
        vm.Relocalize();
        Assert.Equal("Compilation succeeded", vm.Status);
        Assert.Equal("2 artifacts", vm.StatusArtifactCount);
    });

    [Fact]
    public Task A_failed_compile_status_keeps_its_exit_code_in_the_new_language() => English(async () =>
    {
        using var dir = new TempDir();
        var cfg = dir.Write("demo.thconfig", "source demo.th\n");
        var vm = Build(new FixedCompiler(3));
        await vm.RunBuildAsync(cfg);
        Assert.Equal("Compilation failed (exit 3).", vm.Status);

        Tr.Culture = Ro;
        vm.Relocalize();
        Assert.Equal("Compilarea a eșuat (cod de ieșire 3).", vm.Status);
        Assert.Equal(string.Empty, vm.StatusArtifactCount);
    });

    [Fact]
    public Task A_status_that_is_not_a_resource_text_is_left_alone() => English(async () =>
    {
        using var dir = new TempDir();
        var cfg = dir.Write("demo.thconfig", "source demo.th\n");
        var vm = Build(new FixedCompiler(0, Path.Combine(dir.Path, "a.lox")));
        await vm.RunBuildAsync(cfg);
        vm.Status = "invented tool message";

        Tr.Culture = Ro;
        vm.Relocalize();
        Assert.Equal("invented tool message", vm.Status);
        Assert.Equal(string.Empty, vm.StatusArtifactCount);
    });

    [Fact]
    public void The_time_mode_options_follow_the_mode_order_and_name_themselves_in_both_languages() =>
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () => UiThread.Run(() =>
        {
            var vm = new BuildViewModel();
            Assert.Equal(Enum.GetValues<TimeColumnMode>().Length, vm.TimeColumnModeOptions.Count);
            Assert.Equal(["Since last line", "Timestamp", "Since compilation start"], vm.TimeColumnModeOptions.Select(o => o.ToString()));
            vm.TimeColumnModeIndex = 2;
            Assert.Equal(TimeColumnMode.SinceStart, vm.TimeColumnMode);

            Tr.Culture = Ro;
            Assert.Equal(["De la ultima linie", "Marcaj temporal", "De la începutul compilării"], vm.TimeColumnModeOptions.Select(o => o.ToString()));
        }));

    [Fact]
    public void The_time_mode_box_is_drawn_again_in_the_new_language() =>
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () => UiThread.Run(() =>
        {
            // The box shows SelectionBoxItem. A ComboBoxItem with bound text put a copy of that text there,
            // which a language switch did not reach. The option item stays there and is drawn again.
            var combo = new ComboBox { ItemsSource = new BuildViewModel().TimeColumnModeOptions, SelectedIndex = 0 };
            var shown = new TextBlock { DataContext = combo.SelectionBoxItem };   // the item template
            shown.Bind(TextBlock.TextProperty, (Avalonia.Data.BindingBase)new LocOptionExtension("Comp_Time").ProvideValue(null!));
            Assert.Equal("Since last line", shown.Text);

            var legacy = new ComboBox();
            var item = new ComboBoxItem();
            item.Bind(ContentControl.ContentProperty, (Avalonia.Data.BindingBase)new LocExtension("Comp_TimeLast").ProvideValue(null!));
            legacy.Items.Add(item);
            legacy.SelectedIndex = 0;

            Tr.Culture = Ro;
            LocProxy.Instance.Invalidate();
            Assert.Equal("De la ultima linie", shown.Text);
            Assert.Equal("De la ultima linie", combo.SelectionBoxItem?.ToString());   // the accessible value
            Assert.Equal("De la ultima linie", item.Content);
            Assert.Equal("Since last line", legacy.SelectionBoxItem);   // why the box stayed English
        }));

    [Fact]
    public void Time_column_values_use_the_UI_decimal_separator() =>
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            Assert.Equal("+1.5 s", CompilerOutputRow.FormatElapsed(TimeSpan.FromSeconds(1.5)));
            Assert.Equal("+250 ms", CompilerOutputRow.FormatElapsed(TimeSpan.FromMilliseconds(250)));
            Tr.Culture = Ro;
            Assert.Equal("+1,5 s", CompilerOutputRow.FormatElapsed(TimeSpan.FromSeconds(1.5)));
            Assert.Equal("+12 s", CompilerOutputRow.FormatElapsed(TimeSpan.FromSeconds(12)));
        });

    [Fact]
    public void Display_lengths_use_the_UI_decimal_separator_whatever_the_ambient_culture() =>
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            var ambient = CultureInfo.CurrentCulture;
            try
            {
                // The ambient culture is not the UI language: the app sets only Tr.Culture on a switch.
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.Equal("6.8 m", ProjectFormat.Length(6.8));
                Assert.Equal("1.25 km", ProjectFormat.Length(1250));
                Tr.Culture = Ro;
                Assert.Equal("6,8 m", ProjectFormat.Length(6.8));
                Assert.Equal("54,0 m", ProjectFormat.Length(54));
                Assert.Equal("1,25 km", ProjectFormat.Length(1250));

                CultureInfo.CurrentCulture = Ro;
                Tr.Culture = CultureInfo.InvariantCulture;
                Assert.Equal("6.8 m", ProjectFormat.Length(6.8));
            }
            finally { CultureInfo.CurrentCulture = ambient; }
        });

    [Fact]
    public void A_document_keeps_its_view_across_a_language_switch_and_a_scroll_shows_the_source()
    {
        using var dir = new TempDir();
        const string text = "survey cave\nendsurvey\n";
        var path = dir.Write("cave.th", text);
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () => UiThread.Run(() =>
        {
            var docs = new ScriptedDocumentService();
            var doc = docs.OpenTextDocument(path, text);
            Assert.Equal(0, doc.ViewTabIndex);   // Source
            doc.ViewTabIndex = 2;                // Measurements

            Tr.Culture = Ro;
            LocProxy.Instance.Invalidate();
            doc.Relocalize();
            Assert.Equal(2, doc.ViewTabIndex);

            var at = new SourceLocation(1, 1);
            doc.RequestScrollTo(new SourceSpan(path, at, at, 0, 0));
            Assert.Equal(0, doc.ViewTabIndex);
        }));
    }
}
