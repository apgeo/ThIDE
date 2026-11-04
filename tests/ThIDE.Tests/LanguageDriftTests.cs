// The UI language is one process-wide value (Tr.Culture). It used to be the ambient
// CultureInfo.CurrentUICulture, an AsyncLocal: a session started in Romanian and switched to English
// read Romanian again on every flow captured at startup (menu headers re-read after a Save, say).

using System.Globalization;
using System.Resources;
using ThIDE.Resources;
using ThIDE.Services;

namespace ThIDE.Tests;

/// <summary>
/// Tests that set the process-wide UI language run alone: every Tr.Get in a parallel test class would
/// otherwise read their language.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiLanguageCollection
{
    public const string Name = "UiLanguage";

    private static readonly System.Reflection.FieldInfo Field =
        typeof(Tr).GetField("_culture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    /// <summary>The language set on Tr, or null before any was (Tr then reads the ambient culture).</summary>
    public static CultureInfo? Pinned
    {
        get => (CultureInfo?)Field.GetValue(null);
        set => Field.SetValue(null, value);
    }

    /// <summary>Runs <paramref name="body"/> with <paramref name="culture"/> as the UI language.</summary>
    public static void In(CultureInfo culture, Action body)
    {
        var saved = Pinned;
        Tr.Culture = culture;
        try { body(); }
        finally { Pinned = saved; }
    }
}

[Collection(UiLanguageCollection.Name)]
public sealed class LanguageDriftTests
{
    private static readonly ResourceManager Rm = new("ThIDE.Resources.Strings", typeof(Strings).Assembly);
    private static string En(string key) => Rm.GetString(key, CultureInfo.InvariantCulture)!;

    [Fact]
    public async Task A_switch_to_English_reaches_a_flow_captured_while_the_app_was_Romanian()
    {
        var saved = UiLanguageCollection.Pinned;
        UiLanguageCollection.Pinned = null;   // a fresh process: no language applied yet
        try
        {
            var language = new LanguageService();
            language.SetLanguage("ro");                       // the saved language, applied at startup
            var startup = ExecutionContext.Capture()!;        // what a continuation or timer set up then keeps
            await Task.Run(() => language.SetLanguage("en")); // Settings ▸ OK, on another flow

            string? tr = null, proxy = null, localizer = null;
            ExecutionContext.Run(startup, _ =>
            {
                tr = Tr.Get("Menu_File");
                proxy = LocProxy.Instance["Menu_Build"];          // {l:Loc} bindings
                localizer = new TrLocalizer()["Menu_Edit"].Value; // view models' injected localizer
            }, null);

            Assert.Equal(En("Menu_File"), tr);
            Assert.Equal(En("Menu_Build"), proxy);
            Assert.Equal(En("Menu_Edit"), localizer);
            Assert.Equal("en", language.CurrentCulture.Name);
        }
        finally { UiLanguageCollection.Pinned = saved; }
    }

    [Fact]
    public void The_injected_localizer_follows_the_language_and_reports_missing_keys()
    {
        UiLanguageCollection.In(new CultureInfo("ro"), () =>
        {
            var l = new TrLocalizer();
            Assert.Equal("_Fișier", l["Menu_File"].Value);
            Assert.False(l["Menu_File"].ResourceNotFound);
            Assert.True(l["No_Such_Key"].ResourceNotFound);
            Assert.Equal("No_Such_Key", l["No_Such_Key"].Value);
            Assert.Contains(l.GetAllStrings(true), s => s is { Name: "Menu_File", Value: "_Fișier" });
        });
    }
}
