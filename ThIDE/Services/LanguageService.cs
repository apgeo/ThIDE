// Implementation Plan �7.6 � runtime UI language switching.
// Coordinate/measurement display uses invariant numeric formatting elsewhere.

using System;
using System.Globalization;

namespace ThIDE.Services;

public interface ILanguageService
{
    CultureInfo CurrentCulture { get; }
    event EventHandler? LanguageChanged;
    void SetLanguage(string cultureName);
}

public sealed class LanguageService : ILanguageService
{
    public CultureInfo CurrentCulture { get; private set; } = ThIDE.Resources.Tr.Culture;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        ThIDE.Resources.Tr.Culture = culture;   // the one value every UI string lookup reads
        if (CurrentCulture.Name == culture.Name) return;

        CurrentCulture = culture;
        // Not CultureInfo.CurrentUICulture: it is an AsyncLocal, so setting it here (at startup, or
        // from a click handler) left every flow captured earlier on the old language, and bindings
        // re-read on such a flow switched the menus back. The process default reaches every thread
        // that has no culture of its own, for the framework and anything else that reads the ambient one.
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        // Refresh every {l:Loc} binding (already-loaded views) before notifying VM listeners.
        ThIDE.Resources.LocProxy.Instance.Invalidate();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
