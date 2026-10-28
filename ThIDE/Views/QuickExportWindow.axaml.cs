using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Views;

public partial class QuickExportWindow : Window
{
    public QuickExportWindow()
    {
        InitializeComponent();
        DataContext = new QuickExportViewModel();
    }

    /// <summary>Set to true when the user confirmed (so the caller runs the export).</summary>
    public bool Confirmed { get; private set; }

    public QuickExportViewModel Model => (QuickExportViewModel)DataContext!;

    private void OnExport(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Saves the composed block as a thconfig fragment in the project's snippets.json (the user's
    /// own when no project is open), so Generate code ▸ Insert snippet offers it in every thconfig.
    /// </summary>
    private async void OnSaveFragment(object? sender, RoutedEventArgs e)
    {
        var name = await new InputDialog(Tr.Get("QExport_SaveFragment"), Tr.Get("QExport_FragmentName"), "export-" + Model.OutputFileName.Split('.')[0])
            .ShowAsync(this);
        if (string.IsNullOrWhiteSpace(name)) return;

        var session = AppServices.Provider.GetService<IWorkspaceSession>();
        var settings = AppServices.Provider.GetService<IAppSettingsService>();
        var notifications = AppServices.Provider.GetService<INotificationService>();
        var path = Therion.Workspace.ProjectLocalConfig.PathFor(session?.RootPath,
                       settings?.Current.ProjectConfigLocation ?? Therion.Workspace.ProjectConfigLocation.ProjectFolder, "snippets.json")
                   ?? ThIDE.Editor.TherionSnippets.UserSnippetPath();
        try
        {
            var existing = File.Exists(path) ? File.ReadAllText(path) : null;
            var (json, error) = Therion.Workspace.Templates.FragmentStore.Append(existing, name.Trim(), Model.ComposeBlock().Split('\n')[0],
                Therion.Workspace.Templates.FragmentStore.TemplateFromText(Model.ComposeBlock()));
            if (json is null)
            {
                notifications?.Error(Tr.Get("QExport_SaveFragment"), $"{Path.GetFileName(path)}: {error}");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json);
            ThIDE.Editor.TherionSnippets.Reload();
            notifications?.Success(Tr.Get("QExport_SaveFragment"), string.Format(Tr.Get("QExport_FragmentSaved"), name.Trim(), path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notifications?.Error(Tr.Get("QExport_SaveFragment"), ex.Message);
        }
    }
}
