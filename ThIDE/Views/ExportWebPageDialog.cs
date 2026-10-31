// Modal dialog (code-only, no .axaml) collecting the options of "export the 3D model as a web
// page": title, text, places, the opening view, and whether the model is carried inside the page.
// Returns the choices, or null when cancelled; the folder is picked afterwards by the caller.

using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ThIDE.Resources;

namespace ThIDE.Views;

public sealed record ExportWebPageChoices(string Title, string? Description, bool IncludePlaces, bool EmbedModel, bool CurrentView);

public sealed class ExportWebPageDialog : Window
{
    public ExportWebPageDialog(string defaultTitle)
    {
        Title = Tr.Get("M3D_ExportTitle");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = new TextBox { Text = defaultTitle };
        title.AttachedToVisualTree += (_, _) => { title.Focus(); title.SelectAll(); };
        var description = new TextBox { AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Height = 64 };
        var places = new CheckBox { Content = Tr.Get("M3D_ExportPlaces"), IsChecked = true };
        var view = new CheckBox { Content = Tr.Get("M3D_ExportCurrentView"), IsChecked = true };
        var embed = new CheckBox { Content = Tr.Get("M3D_ExportEmbed"), IsChecked = false };
        foreach (var c in new[] { places, view, embed })
            c.Content = new TextBlock { Text = (string)c.Content!, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        var ok = new Button { Content = Tr.Get("M3D_ExportChooseFolder"), IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = Tr.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Close(new ExportWebPageChoices(
            title.Text?.Trim() is { Length: > 0 } t ? t : defaultTitle,
            string.IsNullOrWhiteSpace(description.Text) ? null : description.Text.Trim(),
            places.IsChecked == true, embed.IsChecked == true, view.IsChecked == true));
        cancel.Click += (_, _) => Close(null);

        AutomationProperties.SetAutomationId(this, "ExportWebPage.Window");
        AutomationProperties.SetAutomationId(title, "ExportWebPage.Title");
        AutomationProperties.SetName(title, Tr.Get("M3D_ExportPageTitle"));
        AutomationProperties.SetAutomationId(description, "ExportWebPage.Description");
        AutomationProperties.SetName(description, Tr.Get("M3D_ExportDescription"));
        AutomationProperties.SetAutomationId(places, "ExportWebPage.Places");
        AutomationProperties.SetAutomationId(view, "ExportWebPage.CurrentView");
        AutomationProperties.SetAutomationId(embed, "ExportWebPage.Embed");
        AutomationProperties.SetAutomationId(ok, "ExportWebPage.Ok");
        AutomationProperties.SetAutomationId(cancel, "ExportWebPage.Cancel");

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Tr.Get("M3D_ExportIntro"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8 },
                new TextBlock { Text = Tr.Get("M3D_ExportPageTitle") },
                title,
                new TextBlock { Text = Tr.Get("M3D_ExportDescription") },
                description,
                places,
                view,
                embed,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Avalonia.Thickness(0, 6, 0, 0),
                    Children = { cancel, ok },
                },
            },
        };

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(null); };
    }

    public Task<ExportWebPageChoices?> ShowAsync(Window owner) => ShowDialog<ExportWebPageChoices?>(owner);
}
