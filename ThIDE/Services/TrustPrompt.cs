using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ThIDE.Resources;
using ThIDE.Views;

namespace ThIDE.Services;

/// <summary>The "Trust this folder?" question, asked over the main window.</summary>
public static class TrustPrompt
{
    public static Task<bool> AskAsync(string folder, string reason) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
            return false;
        var dialog = new ConfirmDialog(Tr.Get("Trust_Title"), string.Format(Tr.Get("Trust_Message"), folder, reason), Tr.Get("Trust_Confirm"));
        return await dialog.ShowAsync(owner);
    });
}
