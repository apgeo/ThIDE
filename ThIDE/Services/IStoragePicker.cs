// Implementation Plan �7.3 � open a file / folder from the UI.
// The actual `IStorageProvider` lives on Avalonia's TopLevel, so the View
// is the only place that can ask for files. We expose a thin abstraction so
// the ViewModel can stay UI-agnostic and unit-testable.

using System.Threading.Tasks;

namespace ThIDE.Services;

public interface IStoragePicker
{
    /// <summary>Show an "Open File" dialog. Returns the picked absolute path, or null if cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title);

    /// <summary>Show an "Open File" dialog filtered to .thconfig project files (#7).</summary>
    Task<string?> PickOpenThconfigAsync(string title);

    /// <summary>Show an "Open Folder" dialog. Returns the picked absolute path, or null if cancelled.</summary>
    Task<string?> PickOpenFolderAsync(string title);

    /// <summary>
    /// Show a "Save File" dialog, starting in <paramref name="startFolder"/> when given. Returns the
    /// chosen absolute path, or null if cancelled.
    /// </summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null);

    /// <summary>Show an "Open File" dialog filtered to saved layout files (*.thlayout).</summary>
    Task<string?> PickOpenLayoutAsync(string title);

    /// <summary>Show a "Save File" dialog filtered to saved layout files (*.thlayout).</summary>
    Task<string?> PickSaveLayoutAsync(string title, string suggestedName);
}
