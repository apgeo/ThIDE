// The core commands sit where users look for them: File has Save / Save As / Save All / Close Tab,
// Edit has Undo / Redo and the Go-to group, Settings is a normal menu, Compile lists Recompile, every
// top-level header has an access key and no two siblings share one (English and Romanian), toolbar
// tooltips name a shortcut once, and the command palette lists the symbol commands with their keys.
// Menu structure is read from MainWindow.axaml and the resx files (the {l:…} headers are bindings,
// not compiled symbols); behaviour is tested on the view model and the document service.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.QuickPick;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class CoreCommandPlacementTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Automation = "clr-namespace:Avalonia.Automation;assembly=Avalonia.Controls";

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    private static Dictionary<string, string> Resx(string file) =>
        XDocument.Load(RepoFile("ThIDE", "Resources", file)).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);

    private static XElement MainMenu() =>
        XDocument.Load(RepoFile("ThIDE", "Views", "MainWindow.axaml")).Descendants(Av + "Menu")
            .Single(m => (string?)m.Attribute(X + "Name") == "MainMenu");

    private static IEnumerable<XElement> Items(XElement parent) => parent.Elements(Av + "MenuItem");

    private static string? Id(XElement item) =>
        (string?)item.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId");

    private static XElement Item(XElement parent, string automationId) =>
        Items(parent).Single(i => Id(i) == automationId);

    // The resx key behind a header: {l:Loc Key}, or a MainWindowViewModel label property that reads one.
    private static readonly Lazy<Dictionary<string, string>> VmLabelKeys = new(() =>
        Regex.Matches(File.ReadAllText(RepoFile("ThIDE", "ViewModels", "MainWindowViewModel.cs")),
                @"public string (\w+)\s*=>\s*L\(""(\w+)""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value));

    private static string? HeaderKey(XElement item)
    {
        var header = (string?)item.Attribute("Header") ?? string.Empty;
        if (Regex.Match(header, @"^\{l:Loc\s+(\w+)\}$") is { Success: true } loc) return loc.Groups[1].Value;
        if (Regex.Match(header, @"^\{Binding\s+(\w+)\}$") is { Success: true } b
            && VmLabelKeys.Value.TryGetValue(b.Groups[1].Value, out var key)) return key;
        return null;   // a literal ("KML…") or a computed header (workspace undo, build targets)
    }

    private static char? AccessKey(string text)
    {
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '_') continue;
            if (text[i + 1] == '_') { i++; continue; }
            return char.ToLower(text[i + 1], CultureInfo.InvariantCulture);
        }
        return null;
    }

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.ro.resx")]
    public void Every_top_level_menu_has_an_access_key_and_no_siblings_share_one(string resx)
    {
        var strings = Resx(resx);
        var problems = new List<string>();

        void Check(XElement parent, string path, bool topLevel)
        {
            var seen = new Dictionary<char, string>();
            foreach (var item in Items(parent))
            {
                var key = HeaderKey(item);
                var text = key is not null && strings.TryGetValue(key, out var v) ? v : null;
                var ak = text is null ? null : AccessKey(text);
                if (topLevel && ak is null) problems.Add($"{path}{Id(item)} ('{text}') has no access key");
                if (ak is { } c)
                {
                    if (seen.TryGetValue(c, out var other)) problems.Add($"{path}: '{other}' and '{text}' both use '{c}'");
                    else seen[c] = text!;
                }
                Check(item, $"{path}{Id(item)} > ", topLevel: false);
            }
        }

        Check(MainMenu(), string.Empty, topLevel: true);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void File_Edit_Compile_and_Settings_list_the_core_commands_with_their_shortcuts()
    {
        var menu = MainMenu();
        string Gesture(XElement parent, string id) => (string?)Item(parent, id).Attribute("InputGesture") ?? string.Empty;

        var file = Item(menu, "Menu.File");
        Assert.Equal("{l:Gesture Save}", Gesture(file, "Menu.File.Save"));
        Assert.Equal("{l:Gesture SaveAs}", Gesture(file, "Menu.File.SaveAs"));
        Assert.Equal("{l:Gesture SaveAll}", Gesture(file, "Menu.File.SaveAll"));
        Assert.Equal("{l:Gesture CloseTab}", Gesture(file, "Menu.File.CloseTab"));

        var edit = Item(menu, "Menu.Edit");
        Assert.Equal(new[] { "Menu.Edit.Undo", "Menu.Edit.Redo" }, Items(edit).Take(2).Select(Id));
        Assert.Equal("{l:Gesture GoToDefinition}", Gesture(edit, "Menu.Edit.GoToDefinition"));
        Assert.Equal("{l:Gesture PeekDefinition}", Gesture(edit, "Menu.Edit.PeekDefinition"));
        Assert.Equal("{l:Gesture FindReferences}", Gesture(edit, "Menu.Edit.FindReferences"));
        Assert.Equal("{l:Gesture RenameSymbol}", Gesture(edit, "Menu.Edit.RenameSymbol"));

        var compile = Item(menu, "Menu.Build");
        Assert.Equal("{l:Gesture Rebuild}", Gesture(compile, "Menu.Build.Rebuild"));
        Assert.Equal("{Binding Build.RebuildCommand}", (string?)Item(compile, "Menu.Build.Rebuild").Attribute("Command"));

        // Settings opens a dropdown like every other header (ui_menu "Settings > Preferences…" resolves).
        var settings = Item(menu, "Menu.Settings");
        Assert.Null(settings.Attribute("Click"));
        Assert.Equal(new[] { "Menu.Settings.Preferences", "Menu.Settings.KeyboardShortcuts" }, Items(settings).Select(Id));
        Assert.Equal("{l:Gesture OpenPreferences}", Gesture(settings, "Menu.Settings.Preferences"));
    }

    [Fact]
    public void New_commands_ship_with_the_conventional_shortcuts()
    {
        var defaults = new JsonKeyboardShortcutService(TestProfile.NewPath("keyboard.json")).Defaults;
        Assert.Equal("Ctrl+Shift+S", defaults[ShellCommandIds.SaveAs]);
        Assert.Equal("Ctrl+W", defaults[ShellCommandIds.CloseTab]);
        Assert.Equal("Ctrl+,", KeyGestureText.Format(defaults[ShellCommandIds.OpenPreferences], macOS: false));
    }

    // TipProxy appends the live gesture, so a value that names one shows it twice ("Save (Ctrl+S) (Ctrl+S)").
    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.ro.resx")]
    public void Toolbar_tooltip_strings_name_no_shortcut(string resx)
    {
        var withGesture = Resx(resx)
            .Where(kv => kv.Key.StartsWith("Tb_", StringComparison.Ordinal)
                         && Regex.IsMatch(kv.Value, @"\((Ctrl|Alt|Shift|F\d)"))
            .Select(kv => $"{kv.Key} = {kv.Value}")
            .ToList();
        Assert.True(withGesture.Count == 0, string.Join(Environment.NewLine, withGesture));
    }

    [Fact]
    public void Toolbar_tooltip_shows_the_shortcut_once_and_none_for_an_unbound_command()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            TipProxy.Instance.Attach(new JsonKeyboardShortcutService(TestProfile.NewPath("keyboard.json")));
            // The stored chord is the same everywhere; macOS shows it with its own symbols ("⌃S").
            Assert.Equal($"Save ({KeyGestureText.Format("Ctrl+S")})", TipProxy.Instance["Tb_Save"]);
            Assert.Equal("Open File", TipProxy.Instance["Tb_OpenFile"]);
        });
    }

    // ---- command palette ----

    private static IReadOnlyList<QuickPickItem> Palette(string query)
    {
        GestureProxy.Instance.Attach(new JsonKeyboardShortcutService(TestProfile.NewPath("keyboard.json")));
        var vm = MainWindowViewModelHost.Create(new NullDocumentService());
        var palette = new CommandPaletteProvider(vm, null, null, null, _ => { }).CreatePalette(query);
        return palette.Results.ToList();
    }

    [Fact]
    public void Palette_lists_the_symbol_commands_with_their_shortcuts() =>
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            var refer = Palette("refer");
            Assert.Equal("Find References", refer[0].Title);
            Assert.Equal(KeyGestureText.Format("Shift+F12"), refer[0].Gesture);

            var defin = Palette("defin").Take(2).Select(i => i.Title).ToList();
            Assert.Contains("Go to Definition", defin);
            Assert.Contains("Peek Definition", defin);
            Assert.Equal(KeyGestureText.Format("F12"), Palette("go to definition")[0].Gesture);

            var rename = Assert.Single(Palette("rename symbol"));
            Assert.Equal(KeyGestureText.Format("F2"), rename.Gesture);
        });

    [Fact]
    public void Fuzzy_match_rewards_word_starts_and_ignores_a_thin_scatter_over_a_long_name()
    {
        Assert.Null(QuickPickMatcher.Score("defin", "build target: export model -fmt survex -o cave/model.3d", "build"));
        var acronym = QuickPickMatcher.Score("gtd", "go to definition", "editor");
        var inside = QuickPickMatcher.Score("gtd", "getting tidy", "editor");
        Assert.NotNull(acronym);
        Assert.NotNull(inside);
        Assert.True(acronym > inside, $"word-start match {acronym} should beat in-word match {inside}");
        // File names keep their fuzzy matches.
        Assert.NotNull(QuickPickMatcher.Score("svmn", "survey_main.th", "cave"));
    }

    // ---- File ▸ Save As / Save All, Settings ▸ Keyboard Shortcuts ----

    private sealed class StubResolver : IProjectEntryPointResolver
    {
        public ValueTask<EntryPointResolution> ResolveAsync(string pathOrFolder, CancellationToken ct = default)
            => new(new EntryPointResolution(System.Collections.Immutable.ImmutableArray<string>.Empty, pathOrFolder,
                System.Collections.Immutable.ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentService NewDocuments() =>
        new(new StubResolver(), new WorkspaceSessionService(new StubSniffer(), new FakeSettings()));

    [Fact]
    public async Task Save_As_writes_the_edits_to_the_new_file_and_leaves_the_original_as_saved()
    {
        using var dir = new TempDir();
        var original = dir.Write("a.th", "survey a\nendsurvey\n");
        var copy = Path.Combine(dir.Path, "b.th");
        var svc = NewDocuments();
        await svc.OpenFileAsync(original);
        var doc = Assert.Single(svc.Documents);
        doc.DocumentText = "survey a\n  # edited\nendsurvey\n";

        var saved = await svc.SaveAsAsync(doc, copy);

        Assert.NotNull(saved);
        Assert.Equal(Path.GetFullPath(copy), saved!.FilePath);
        Assert.Equal("survey a\n  # edited\nendsurvey\n", File.ReadAllText(copy));
        Assert.Equal("survey a\nendsurvey\n", File.ReadAllText(original));
        Assert.False(saved.IsDirty);
        Assert.DoesNotContain(doc, svc.Documents);
    }

    [Fact]
    public async Task Save_As_over_an_open_file_shows_the_new_text_in_its_tab()
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "survey a\nendsurvey\n");
        var b = dir.Write("b.th", "survey b\nendsurvey\n");
        var svc = NewDocuments();
        await svc.OpenFileAsync(a);
        await svc.OpenFileAsync(b);
        var docA = svc.Documents.Single(d => d.FilePath == a);
        var docB = svc.Documents.Single(d => d.FilePath == b);
        docA.DocumentText = "survey a\n  # moved\nendsurvey\n";

        var saved = await svc.SaveAsAsync(docA, b);

        Assert.Same(docB, saved);
        Assert.Equal("survey a\n  # moved\nendsurvey\n", docB.DocumentText);
        Assert.Equal(docB.DocumentText, File.ReadAllText(b));
        Assert.Single(svc.Documents);
    }

    [Fact]
    public Task Save_All_writes_every_modified_document() => UiThread.RunAsync(async () =>
    {
        using var dir = new TempDir();
        var a = dir.Write("a.th", "survey a\nendsurvey\n");
        var b = dir.Write("b.th", "survey b\nendsurvey\n");
        var svc = NewDocuments();
        await svc.OpenFileAsync(a);
        await svc.OpenFileAsync(b);
        foreach (var d in svc.Documents) d.DocumentText = d.DocumentText.Replace("endsurvey", "  # x\nendsurvey");
        var vm = MainWindowViewModelHost.Create(svc);

        await vm.SaveAllCommand.ExecuteAsync(null);

        Assert.Contains("# x", File.ReadAllText(a));
        Assert.Contains("# x", File.ReadAllText(b));
        Assert.All(svc.Documents, d => Assert.False(d.IsDirty));
    });

    [Fact]
    public void Keyboard_Shortcuts_opens_Settings_at_the_keyboard_section()
    {
        var vm = MainWindowViewModelHost.Create(new NullDocumentService());
        var sections = new List<string?>();
        vm.ShowPreferencesRequested += (_, s) => sections.Add(s);

        vm.OpenKeyboardShortcutsCommand.Execute(null);
        vm.OpenPreferencesCommand.Execute(null);

        Assert.Equal(new[] { "keyboard", null }, sections);
    }
}
