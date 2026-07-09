// Avalonia 12 IStorageProvider adapter for the host-agnostic IStoragePicker.
// Lives on the View side because StorageProvider hangs off TopLevel.

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ThIDE.Services;

namespace ThIDE.Views;

public sealed class AvaloniaStoragePicker : IStoragePicker
{
    private readonly TopLevel _topLevel;

    public AvaloniaStoragePicker(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        var sp = _topLevel.StorageProvider;
        if (sp is null) return null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_TherionFiles"))
                {
                    Patterns = new[] { "*.th", "*.th2", "*.thconfig", "*.thc", "*.xvi", "thconfig" },
                },
                FilePickerFileTypes.All,
            },
        });
        if (files is null || files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    public async Task<string?> PickOpenThconfigAsync(string title)
    {
        var sp = _topLevel.StorageProvider;
        if (sp is null) return null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(ThIDE.Resources.Tr.Get("Pick_TherionConfig"))
                {
                    Patterns = new[] { "*.thconfig", "*.thc", "thconfig" },
                },
                FilePickerFileTypes.All,
            },
        });
        if (files is null || files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    public async Task<string?> PickOpenFolderAsync(string title)
    {
        var sp = _topLevel.StorageProvider;
        if (sp is null) return null;
        var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        if (folders is null || folders.Count == 0) return null;
        return folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        var sp = _topLevel.StorageProvider;
        if (sp is null) return null;
        var ext = System.IO.Path.GetExtension(suggestedName).TrimStart('.');
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = ext,
        });
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType LayoutFileType => new(ThIDE.Resources.Tr.Get("Pick_LayoutFiles"))
    {
        Patterns = new[] { "*" + ThIDE.Services.LayoutProfileFile.Extension },
    };

    public async Task<string?> PickOpenLayoutAsync(string title)
    {
        var sp = _topLevel.StorageProvider;
        if (sp is null) return null;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { LayoutFileType, FilePickerFileTypes.All },
        });
        if (files is null || files.Count == 0) return null;
        return files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveLayoutAsync(string title, string suggestedName)
    {
        var sp = _topLevel.StorageProvider;
        if (sp is null) return null;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = ThIDE.Services.LayoutProfileFile.Extension.TrimStart('.'),
            FileTypeChoices = new[] { LayoutFileType },
        });
        return file?.TryGetLocalPath();
    }
}
