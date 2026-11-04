// Names from the user's data (files, folders, surveys, stations, tasks) shown as the content of a
// Button, CheckBox or MenuItem. A string content goes through AccessText, which reads the first '_'
// as an access-key marker and hides it ("trip_2026.th" showed as "trip2026.th"). A TextBlock has no
// access keys, so the name is shown as written; the accessible name is set to the same text.

using Avalonia.Automation;
using Avalonia.Controls;

namespace ThIDE.Views;

internal static class DataLabel
{
    /// <summary>The content for <paramref name="owner"/> that shows <paramref name="text"/> verbatim.</summary>
    public static TextBlock For(Control owner, string text)
    {
        AutomationProperties.SetName(owner, text);
        return new TextBlock { Text = text };
    }
}
