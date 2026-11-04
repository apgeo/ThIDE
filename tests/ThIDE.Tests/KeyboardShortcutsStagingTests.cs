// Settings ▸ Keyboard: edits are staged until OK, a gesture another command already has is asked
// about instead of taken (which left the other binding dead), and the rows read as the user reads
// them — the command's name in the UI language and the key as printed on it.

using System.Globalization;
using System.Linq;
using System.Reflection;
using Therion.Processing.Abstractions;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class KeyboardShortcutsStagingTests
{
    private static JsonKeyboardShortcutService NewService(string path) => new(path);

    private static KeyboardShortcutRow Row(KeyboardShortcutsViewModel vm, string id) =>
        vm.Rows.Single(r => r.CommandId == id);

    [Fact]
    public void Edits_reach_the_service_only_on_commit_and_discard_drops_them()
    {
        var path = TestProfile.NewPath("keyboard.json");
        var service = NewService(path);
        var vm = new KeyboardShortcutsViewModel(service);

        Row(vm, ShellCommandIds.DuplicateLines).Capture("Ctrl+Alt+D");
        Assert.Equal("", service.Gestures[ShellCommandIds.DuplicateLines]);
        Assert.False(System.IO.File.Exists(path));
        Assert.True(vm.HasChanges);

        vm.Discard();   // Cancel / Esc, seen by the next opening
        Assert.Equal("", Row(vm, ShellCommandIds.DuplicateLines).Gesture);

        Row(vm, ShellCommandIds.DuplicateLines).Capture("Ctrl+Alt+D");
        vm.Commit();
        Assert.Equal("Ctrl+Alt+D", service.Gestures[ShellCommandIds.DuplicateLines]);
        Assert.Equal("Ctrl+Alt+D", NewService(path).Gestures[ShellCommandIds.DuplicateLines]);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Reset_all_is_staged_too()
    {
        var service = NewService(TestProfile.NewPath("keyboard.json"));
        service.Set(ShellCommandIds.Save, "Ctrl+Alt+S");
        var vm = new KeyboardShortcutsViewModel(service);

        vm.ResetAllCommand.Execute(null);
        Assert.Equal("Ctrl+S", Row(vm, ShellCommandIds.Save).Gesture);
        Assert.Equal("Ctrl+Alt+S", service.Gestures[ShellCommandIds.Save]);

        vm.Commit();
        Assert.Equal("Ctrl+S", service.Gestures[ShellCommandIds.Save]);
    }

    [Fact]
    public void A_gesture_in_use_asks_and_replace_moves_it()
    {
        var service = NewService(TestProfile.NewPath("keyboard.json"));
        var vm = new KeyboardShortcutsViewModel(service);
        var dup = Row(vm, ShellCommandIds.DuplicateLines);

        dup.Capture("Ctrl+Shift+F");   // Find in Files has it

        Assert.True(dup.HasConflict);
        Assert.Equal(ShellCommandIds.FindInFiles, dup.ConflictWith!.CommandId);
        Assert.Contains(dup.ConflictWith.DisplayName, dup.ConflictText);

        dup.ReplaceConflictCommand.Execute(null);
        Assert.False(dup.HasConflict);
        Assert.Equal("", Row(vm, ShellCommandIds.FindInFiles).Gesture);

        vm.Commit();
        Assert.Equal("Ctrl+Shift+F", service.Gestures[ShellCommandIds.DuplicateLines]);
        Assert.Equal("", service.Gestures[ShellCommandIds.FindInFiles]);
    }

    [Fact]
    public void Keep_and_an_undecided_conflict_leave_both_commands_as_they_were()
    {
        var service = NewService(TestProfile.NewPath("keyboard.json"));
        var vm = new KeyboardShortcutsViewModel(service);
        var floatDoc = Row(vm, ShellCommandIds.FloatActiveDocument);
        floatDoc.Capture("Ctrl+Alt+K");

        floatDoc.Capture("Ctrl+S");
        floatDoc.Capture("ctrl+s");    // a second try while asked: still the gesture from before
        floatDoc.KeepConflictCommand.Execute(null);
        Assert.Equal("Ctrl+Alt+K", floatDoc.Gesture);

        floatDoc.Capture("Ctrl+S");
        vm.Commit();                   // OK without an answer
        Assert.Equal("Ctrl+Alt+K", service.Gestures[ShellCommandIds.FloatActiveDocument]);
        Assert.Equal("Ctrl+S", service.Gestures[ShellCommandIds.Save]);
    }

    // Every row has a name from the resources, in both languages.
    [Fact]
    public void Every_command_has_a_localized_name()
    {
        var vm = new KeyboardShortcutsViewModel(NewService(TestProfile.NewPath("keyboard.json")));
        var ids = typeof(ShellCommandIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            foreach (var id in ids) Assert.False(Tr.Find("Kb_Cmd_" + id) is null, $"No Kb_Cmd_{id}");
            vm.Discard();
            Assert.All(vm.Rows, r => Assert.Equal(Tr.Get("Kb_Cmd_" + r.CommandId), r.DisplayName));
            Assert.Equal("Find in Files", Row(vm, ShellCommandIds.FindInFiles).DisplayName);
        });
        UiLanguageCollection.In(new CultureInfo("ro-RO"), () =>
        {
            vm.Discard();
            Assert.Equal("Caută în fișiere", Row(vm, ShellCommandIds.FindInFiles).DisplayName);
            vm.SetFilter("caută");
            Assert.Contains(vm.Rows, r => r.CommandId == ShellCommandIds.FindInFiles);
        });
    }

    [Theory]
    [InlineData("Ctrl+OemCloseBrackets", false, "Ctrl+]")]
    [InlineData("Ctrl+OemQuestion", false, "Ctrl+/")]
    [InlineData("Shift+Alt+F", false, "Shift+Alt+F")]
    [InlineData("Ctrl+D1", false, "Ctrl+1")]
    [InlineData("Ctrl+Shift+F", true, "⌃⇧F")]
    [InlineData("", false, "")]
    [InlineData("not a gesture+", false, "not a gesture+")]
    public void Gestures_show_the_key_as_printed(string stored, bool macOS, string shown) =>
        Assert.Equal(shown, KeyGestureText.Format(stored, macOS));

    [Fact]
    public void Same_gesture_ignores_spelling()
    {
        Assert.True(KeyGestureText.Same("Ctrl+Shift+F", "shift+ctrl+f"));
        Assert.False(KeyGestureText.Same("Ctrl+F", "Ctrl+Shift+F"));
        Assert.False(KeyGestureText.Same("", ""));
    }
}
