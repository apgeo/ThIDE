// Implementation Plan §7.3 — open a file / folder from the UI.
// The actual `IStorageProvider` lives on Avalonia's TopLevel, so the View
// is the only place that can ask for files. We expose a thin abstraction so
// the ViewModel can stay UI-agnostic and unit-testable.

using System.Threading.Tasks;

namespace TherionProc.Services;

public interface IStoragePicker
{
    /// <summary>Show an "Open File" dialog. Returns the picked absolute path, or null if cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title);

    /// <summary>Show an "Open Folder" dialog. Returns the picked absolute path, or null if cancelled.</summary>
    Task<string?> PickOpenFolderAsync(string title);
}
