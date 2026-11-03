// Shared Save / Don't save / Cancel decision for closing documents with unsaved changes — one tab
// (the dock's close guard) or all of them (closing the main window).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ThIDE.ViewModels.Docking;
using ThIDE.Views;

namespace ThIDE.Services;

public static class SaveBeforeClose
{
    /// <summary>
    /// Asks about the dirty ones among <paramref name="documents"/> and returns true when they may close:
    /// none is dirty, the user chose Don't save, or the chosen save left every one of them on disk. A save
    /// that throws, writes nothing (an encoding that cannot hold the text) or is overtaken by new typing
    /// keeps them open. <paramref name="reportError"/> receives the message of a save that threw.
    /// </summary>
    public static async Task<bool> ConfirmAsync(
        IEnumerable<FileDocumentViewModel> documents,
        Func<IReadOnlyList<FileDocumentViewModel>, Task<SaveBeforeBuildChoice>> ask,
        IDocumentService service,
        Action<string>? reportError = null)
    {
        var dirty = documents.Where(d => d.IsDirty).ToList();
        if (dirty.Count == 0) return true;

        switch (await ask(dirty).ConfigureAwait(true))
        {
            case SaveBeforeBuildChoice.Save:
                foreach (var doc in dirty)
                {
                    try { await service.SaveDocumentAsync(doc).ConfigureAwait(true); }
                    catch (Exception ex) { reportError?.Invoke(ex.Message); return false; }
                }
                return dirty.All(d => !d.IsDirty);
            case SaveBeforeBuildChoice.DontSave:
                return true;
            default:
                return false;   // Cancel
        }
    }
}
