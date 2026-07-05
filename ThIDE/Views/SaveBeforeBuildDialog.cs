// Three-way modal shown before a compile when project files have unsaved changes:
// Save & compile / Compile without saving / Cancel. Returns the user's choice.

using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ThIDE.Views;

public enum SaveBeforeBuildChoice { Save, DontSave, Cancel }

public sealed class SaveBeforeBuildDialog : Window
{
    private SaveBeforeBuildChoice _result = SaveBeforeBuildChoice.Cancel;

    public SaveBeforeBuildDialog(string title, string message, string saveLabel, string dontSaveLabel, string cancelLabel)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = message };

        var save = new Button { Content = saveLabel, IsDefault = true, MinWidth = 110 };
        save.Click += (_, _) => { _result = SaveBeforeBuildChoice.Save; Close(); };
        var dontSave = new Button { Content = dontSaveLabel, MinWidth = 110 };
        dontSave.Click += (_, _) => { _result = SaveBeforeBuildChoice.DontSave; Close(); };
        var cancel = new Button { Content = cancelLabel, IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => { _result = SaveBeforeBuildChoice.Cancel; Close(); };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14,
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { save, dontSave, cancel },
                },
            },
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) { _result = SaveBeforeBuildChoice.Cancel; Close(); } };
    }

    public async Task<SaveBeforeBuildChoice> ShowAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result;
    }

    /// <summary>Shows over the main window; returns <paramref name="noWindowResult"/> when there is no
    /// desktop main window (headless / design-time) so callers proceed safely instead of aborting.</summary>
    public static Task<SaveBeforeBuildChoice> ShowOverMainAsync(
        string title, string message, string saveLabel, string dontSaveLabel, string cancelLabel,
        SaveBeforeBuildChoice noWindowResult = SaveBeforeBuildChoice.DontSave)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
            return new SaveBeforeBuildDialog(title, message, saveLabel, dontSaveLabel, cancelLabel).ShowAsync(main);
        return Task.FromResult(noWindowResult);
    }
}
