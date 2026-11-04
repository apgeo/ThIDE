// The Workspace Explorer's Delete, kept out of the view so it runs without one: the confirmation says
// what the delete really does on this OS (to the Recycle Bin / Trash, or for good), and when the trash
// refuses the item it is deleted for good only after a second, explicit yes.

using System;
using System.IO;
using System.Threading.Tasks;
using ThIDE.Resources;

namespace ThIDE.Services;

public static class ExplorerDelete
{
    /// <summary>
    /// Asks, then deletes <paramref name="path"/>. <paramref name="confirm"/> shows a question with the
    /// label of its confirming button and returns the answer. True when the item is gone.
    /// </summary>
    public static async Task<bool> RunAsync(string path, IFileOperations? fileOps,
        Func<string, string, Task<bool>> confirm)
    {
        // The message states whether it's undoable (trash) or permanent, so the user knows the stakes.
        var label = fileOps?.DeleteActionLabel ?? Tr.Get("Ctx_Delete");
        bool undoable = fileOps?.DeleteIsUndoable ?? false;
        var name = Path.GetFileName(path);
        var message = undoable
            ? string.Format(Tr.Get("Explorer_DeleteUndoable"), label, name)
            : string.Format(Tr.Get("Explorer_DeletePermanent"), name);

        if (!await confirm(message, label).ConfigureAwait(true)) return false;
        // Cross-platform delete (recycle bin / trash / permanent) via the OS-specific service.
        if (fileOps?.Delete(path) == true) return true;
        // The trash refused it; the item is still there. Only delete for good if the user says so.
        if (fileOps is null || !undoable) return false;
        var again = string.Format(Tr.Get("Explorer_TrashFailed"), name);
        if (!await confirm(again, Tr.Get("Explorer_DeletePermanently")).ConfigureAwait(true)) return false;
        return fileOps.DeletePermanently(path);
    }
}
