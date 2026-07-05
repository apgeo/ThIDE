// Cross-platform abstraction (Plan: cross-platform refactor) for destructive
// filesystem operations used by the workspace explorer. The recycle-bin / trash
// behaviour differs per OS, so the concrete implementation is chosen at runtime
// by FileOperationsFactory and consumed through this interface via DI. This keeps
// the Win32 P/Invoke (Recycle Bin) and the Unix trash helpers out of the view code.

namespace ThIDE.Services;

public interface IFileOperations
{
    /// <summary>
    /// Deletes a file or folder, preferring the OS trash / recycle bin (undoable)
    /// where available and falling back to a permanent delete otherwise.
    /// Best-effort: returns false on failure rather than throwing.
    /// </summary>
    bool Delete(string path);

    /// <summary>
    /// True when <see cref="Delete"/> routes to an undoable trash/recycle bin on this
    /// platform; false when it deletes permanently. Lets the UI adapt its wording.
    /// </summary>
    bool DeleteIsUndoable { get; }

    /// <summary>
    /// Platform-adapted label for the delete action, e.g. "Move to Recycle Bin"
    /// (Windows), "Move to Trash" (macOS / Linux with a trash backend) or
    /// "Delete Permanently" (no trash backend available).
    /// </summary>
    string DeleteActionLabel { get; }
}
