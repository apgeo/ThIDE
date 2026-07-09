using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Input;
using Therion.Processing.Abstractions;
using ThIDE.Services;
using Xunit;

namespace ThIDE.Tests;

// Guards the keyboard-shortcut map. Both the shell (Window.KeyBindings) and the editor's
// caret-scoped table resolve gestures by parsing the strings in JsonKeyboardShortcutService.
// A typo there fails *silently* — the parse throws, the binding is skipped, and the key simply
// stops working — so these invariants are worth asserting.
public class KeyboardShortcutDefaultsTests
{
    private static JsonKeyboardShortcutService NewService() =>
        // Point at a throwaway path so the user's real keyboard.json is never read or written.
        new(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "keyboard.json"));

    private static IEnumerable<string> AllCommandIds() =>
        typeof(ShellCommandIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Every_non_empty_default_gesture_parses()
    {
        foreach (var (id, gesture) in NewService().Defaults)
        {
            if (string.IsNullOrWhiteSpace(gesture)) continue;
            var ex = Record.Exception(() => KeyGesture.Parse(gesture));
            Assert.True(ex is null, $"Default gesture '{gesture}' for '{id}' does not parse: {ex?.Message}");
        }
    }

    // The Settings ▸ Keyboard grid lists Defaults.Keys, so an id missing from the map is a
    // command the user can never see or bind.
    [Fact]
    public void Every_command_id_has_a_default_entry()
    {
        var defaults = NewService().Defaults;
        var missing = AllCommandIds().Where(id => !defaults.ContainsKey(id)).ToList();
        Assert.True(missing.Count == 0, "Command ids with no default entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Default_gestures_do_not_collide()
    {
        var collisions = NewService().Defaults
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} → {string.Join(" / ", g.Select(kv => kv.Key))}")
            .ToList();
        Assert.True(collisions.Count == 0, "Colliding default gestures: " + string.Join("; ", collisions));
    }

    // The editor registers this as a second gesture for ToggleComment (numpad "/").
    [Fact]
    public void Numpad_divide_alias_parses()
    {
        var gesture = KeyGesture.Parse("Ctrl+Divide");
        Assert.Equal(Key.Divide, gesture.Key);
        Assert.Equal(KeyModifiers.Control, gesture.KeyModifiers);
    }

    // {l:Gesture Xyz} in XAML is a binding path, not a compiled symbol: a typo'd command id still
    // compiles, it just silently never resolves and the menu never shows its chord.
    [Fact]
    public void Menu_gesture_bindings_reference_real_command_ids()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ThIDE.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "ThIDE", "Views", "MainWindow.axaml"));
        var used = System.Text.RegularExpressions.Regex
            .Matches(xaml, @"\{l:Gesture\s+([A-Za-z0-9]+)\s*\}")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        var known = AllCommandIds().ToHashSet(StringComparer.Ordinal);
        var unknown = used.Where(id => !known.Contains(id)).ToList();
        Assert.True(unknown.Count == 0, "{l:Gesture} ids not found in ShellCommandIds: " + string.Join(", ", unknown));
    }

    // Gestures match modifiers exactly, so the three F12-based actions never shadow each other.
    [Fact]
    public void F12_family_is_disambiguated_by_modifiers()
    {
        var defaults = NewService().Defaults;
        Assert.Equal("F12", defaults[ShellCommandIds.GoToDefinition]);
        Assert.Equal("Shift+F12", defaults[ShellCommandIds.FindReferences]);
        Assert.Equal("Alt+F12", defaults[ShellCommandIds.PeekDefinition]);

        var goToDef = KeyGesture.Parse(defaults[ShellCommandIds.GoToDefinition]);
        Assert.Equal(KeyModifiers.None, goToDef.KeyModifiers);
    }
}
