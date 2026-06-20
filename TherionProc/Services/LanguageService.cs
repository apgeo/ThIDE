// Implementation Plan §7.6 — runtime UI language switching.
// Coordinate/measurement display uses invariant numeric formatting elsewhere.

using System;
using System.Globalization;

namespace TherionProc.Services;

public interface ILanguageService
{
    CultureInfo CurrentCulture { get; }
    event EventHandler? LanguageChanged;
    void SetLanguage(string cultureName);
}

public sealed class LanguageService : ILanguageService
{
    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public event EventHandler? LanguageChanged;

    public void SetLanguage(string cultureName)
    {
        var culture = new CultureInfo(cultureName);
        if (CurrentCulture.Name == culture.Name) return;

        CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
