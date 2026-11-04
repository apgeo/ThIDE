// The UI language reaches the user through three paths: {l:Loc}/{l:Gesture} bindings that must re-read
// when the proxies signal a change, dock-panel titles looked up by resource key, and code-built titles
// (Preferences navigation). These tests pin each path under ro-RO. The UI language is process-wide
// (Tr.Culture), so they run in the non-parallel UiLanguage collection.

using System.Globalization;
using System.Reflection;
using System.Resources;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using ThIDE.ViewModels.Docking;
using Therion.Processing.Abstractions;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class LocalizationRefreshTests
{
    private static readonly CultureInfo Ro = new("ro-RO");
    private static readonly ResourceManager Rm = new("ThIDE.Resources.Strings", typeof(Strings).Assembly);

    private static string RoValue(string key) =>
        Rm.GetResourceSet(Ro, createIfNotExists: true, tryParents: true)?.GetString(key)
        ?? throw new InvalidOperationException($"'{key}' has no Romanian value");

    // What the XAML loader does with {l:Loc}/{l:Gesture}: the extension's binding, applied to a property.
    private static BindingBase Bind(MarkupExtension extension) => (BindingBase)extension.ProvideValue(null!);

    private static void InCulture(CultureInfo culture, Action body) =>
        UiLanguageCollection.In(culture, () => UiThread.Run(body));

    [Fact]
    public void LocBinding_ReReadsAfterTheLanguageChanges()
    {
        InCulture(CultureInfo.InvariantCulture, () =>
        {
            var status = new TextBlock();
            status.Bind(TextBlock.TextProperty, Bind(new LocExtension("Status_Length")));
            // A header inside a submenu that has never been opened (the Layout submenu of View).
            var view = new MenuItem();
            var layout = new MenuItem();
            view.Items.Add(layout);
            layout.Bind(MenuItem.HeaderProperty, Bind(new LocExtension("Menu_View_Layout")));
            var compile = new TextBlock();   // a toolbar tooltip ({l:Tip}) follows LocProxy too
            compile.Bind(TextBlock.TextProperty, Bind(new TipExtension("Tb_Compile")));
            Assert.Equal("Length:", status.Text);
            Assert.Equal("Layout", layout.Header);

            Tr.Culture = Ro;
            LocProxy.Instance.Invalidate();

            Assert.Equal(RoValue("Status_Length"), status.Text);
            Assert.Equal("Aspect", layout.Header);
            Assert.StartsWith(RoValue("Tb_Compile"), compile.Text);
        });
    }

    [Fact]
    public void An_option_label_re_reads_after_the_language_changes()
    {
        InCulture(CultureInfo.InvariantCulture, () =>
        {
            // A combo item template ({l:LocOption}): the Group-by box kept "None" after a switch to Romanian.
            var item = new TextBlock { DataContext = "None" };
            item.Bind(TextBlock.TextProperty, Bind(new LocOptionExtension("Opt_GroupBy_")));
            Assert.Equal("None", item.Text);

            Tr.Culture = Ro;
            LocProxy.Instance.Invalidate();

            Assert.Equal("Fără grupare", item.Text);
        });
    }

    [Fact]
    public void GestureBinding_ShowsTheChordOnceTheShortcutServiceIsAttached()
    {
        UiThread.Run(() =>
        {
            // Bound while the main window is built, before the shell attaches the shortcut service.
            var build = new MenuItem();
            build.Bind(MenuItem.InputGestureProperty, Bind(new GestureExtension(ShellCommandIds.Build)));

            GestureProxy.Instance.Attach(new JsonKeyboardShortcutService(TestProfile.NewPath("keyboard.json")));

            Assert.Equal(KeyGesture.Parse("F5"), build.InputGesture);
        });
    }

    public static IEnumerable<object[]> ToolTypes() =>
        typeof(ToolViewModelBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ToolViewModelBase).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => new object[] { t.Name });

    [Theory]
    [MemberData(nameof(ToolTypes))]
    public void ToolTitle_IsAResourceKeyTranslatedInRomanian(string typeName)
    {
        InCulture(Ro, () =>
        {
            var type = typeof(ToolViewModelBase).Assembly.GetTypes().Single(t => t.Name == typeName);
            var tool = (ToolViewModelBase)Activator.CreateInstance(type)!;
            var key = (string)typeof(ToolViewModelBase)
                .GetField("_titleKey", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tool)!;

            Assert.NotNull(Rm.GetString(key, CultureInfo.InvariantCulture));
            Assert.Equal(RoValue(key), tool.Title);
        });
    }

    [Fact]
    public void PreferenceSections_AreAllLocalized()
    {
        InCulture(Ro, () =>
        {
            var vm = new PreferencesViewModel(new AppSettingsService(TestProfile.NewPath("settings.json")));
            var roValues = Rm.GetResourceSet(Ro, true, true)!.Cast<System.Collections.DictionaryEntry>()
                .Select(e => e.Value as string).ToHashSet();

            Assert.All(vm.Sections, s => Assert.Contains(s.Title, roValues));
            Assert.Equal(RoValue("Pref_EditorFeatures"), vm.Sections.Single(s => s.Id == "editorfeatures").Title);
            Assert.Equal(RoValue("Pref_Visualization"), vm.Sections.Single(s => s.Id == "visualization").Title);
            Assert.Equal(RoValue("Pref_Extensions"), vm.Sections.Single(s => s.Id == "extensions").Title);
        });
    }
}
