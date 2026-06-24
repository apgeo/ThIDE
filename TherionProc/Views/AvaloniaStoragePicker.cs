// Avalonia 12 IStorageProvider adapter for the host-agnostic IStoragePicker.
// Lives on the View side because StorageProvider hangs off TopLevel.

using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TherionProc.Services;

namespace TherionProc.Views;

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
                new FilePickerFileType("Therion files")
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
                new FilePickerFileType("Therion configuration")
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
}
