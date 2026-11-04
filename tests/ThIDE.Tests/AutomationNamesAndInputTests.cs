// What screen readers and the automation tree see, and how the automation server drives input:
// quick-pick rows and the thconfig switcher are named by their text (not a class name or a record's
// ToString), a menu command run through the server closes its menu, and a pressed key reaches the
// KeyBindings of the focused element's window as a real key press does.

using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using ThIDE.Services;
using ThIDE.Services.UiAutomation;
using ThIDE.ViewModels.QuickPick;

namespace ThIDE.Tests;

public sealed class AutomationNamesAndInputTests
{
    [Fact]
    public void A_quick_pick_row_is_named_by_its_title() => UiThread.Run(() =>
    {
        var item = new QuickPickItem { Title = "demo.th", Detail = "invented/cave" };
        var row = new ListBoxItem { Content = item };
        Assert.Equal("demo.th", ControlAutomationPeer.CreatePeerForElement(row).GetName());
    });

    [Fact]
    public void The_thconfig_switcher_reads_the_shown_path_not_the_record() => UiThread.Run(() =>
    {
        var candidate = new ThconfigCandidate("/invented/cave/demo.thconfig", "demo.thconfig", false);
        var combo = new ComboBox { ItemsSource = new[] { candidate }, SelectedIndex = 0 };
        Assert.Equal("demo.thconfig", ControlAutomationPeer.CreatePeerForElement(combo).GetProvider<IValueProvider>()!.Value);
        Assert.Equal("demo.thconfig", ControlAutomationPeer.CreatePeerForElement(new ComboBoxItem { Content = candidate }).GetName());
    });

    private sealed class CountingMenu : Menu
    {
        public int Closes;
        public override void Close() { Closes++; base.Close(); }
    }

    [Fact]
    public void Running_a_menu_item_closes_its_menu_like_a_click() => UiThread.Run(() =>
    {
        int clicks = 0;
        var save = new MenuItem { Header = "_Save" };
        save.Click += (_, _) => clicks++;
        var file = new MenuItem { Header = "_File" };
        file.Items.Add(save);
        var menu = new CountingMenu();
        menu.Items.Add(file);

        var node = UiTree.Snapshot(ControlAutomationPeer.CreatePeerForElement(save), 0);
        Assert.Equal("invoked", UiTree.Act(node, "invoke"));

        Assert.Equal(1, clicks);
        Assert.Equal(1, menu.Closes);
    });

    [Fact]
    public void A_pressed_key_runs_the_window_key_binding() => UiThread.Run(() =>
    {
        int compiles = 0, keyDowns = 0;
        var outer = new Border();
        outer.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F5"), Command = new RelayCommand(() => compiles++) });
        var editor = new TextBox();
        outer.Child = editor;
        editor.AddHandler(InputElement.KeyDownEvent, (_, _) => keyDowns++, RoutingStrategies.Bubble, handledEventsToo: true);

        UiTree.Key(editor, "F5");
        Assert.Equal(1, compiles);
        Assert.Equal(0, keyDowns);   // the binding handled it, as the keyboard device would

        UiTree.Key(editor, "F6");   // no binding: the element gets the key
        Assert.Equal(1, compiles);
        Assert.Equal(1, keyDowns);
    });

    [Fact]
    public void A_disabled_key_binding_lets_the_key_through() => UiThread.Run(() =>
    {
        int keyDowns = 0;
        var outer = new Border();
        outer.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F5"), Command = new RelayCommand(() => { }, () => false) });
        var editor = new TextBox();
        outer.Child = editor;
        editor.AddHandler(InputElement.KeyDownEvent, (_, _) => keyDowns++, RoutingStrategies.Bubble, handledEventsToo: true);

        UiTree.Key(editor, "F5");
        Assert.Equal(1, keyDowns);
    });
}
