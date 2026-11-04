// One name per thing, in English and in Romanian: "Settings" (the Settings ▸ Preferences… item stays),
// "Compile" ("Build" only for Therion's build target), "Overview", "Workspace Explorer", "Mainline
// Preview", US spelling, "…"; Romanian "spațiu de lucru", "Renunță" for Cancel and "Anulează" only for
// Undo, the formal plural; counts in the singular or plural they need. Plus the code-built texts that
// stayed English on the Romanian screens. All inputs are invented.

using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Therion.Core;
using Therion.Semantics;
using Therion.Workspace.Import;
using ThIDE.Converters;
using ThIDE.Resources;
using ThIDE.Services;
using ThIDE.ViewModels;

namespace ThIDE.Tests;

[Collection(UiLanguageCollection.Name)]
public sealed class TerminologyTests
{
    private static readonly CultureInfo Ro = new("ro-RO");
    private static readonly ResourceManager Rm = new("ThIDE.Resources.Strings", typeof(Strings).Assembly);

    private static Dictionary<string, string> Table(CultureInfo culture)
    {
        var set = Rm.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry e in set) if (e.Key is string k && e.Value is string v) d[k] = v;
        return d;
    }

    private static readonly Dictionary<string, string> En = Table(CultureInfo.InvariantCulture);
    private static readonly Dictionary<string, string> RoT = Table(new CultureInfo("ro"));

    // Library message templates mirror the libraries' English text exactly; they are not UI wording.
    private static bool IsLibraryTemplate(string key) =>
        key.StartsWith("Diag_Msg_", StringComparison.Ordinal) || key.StartsWith("Diag_Hint_", StringComparison.Ordinal);

    // Therion syntax quoted in UI text keeps Therion's own spelling (centreline, metres).
    private static readonly Regex TherionWords = new(@"\b(end)?centreline\b|\bmetres\b", RegexOptions.CultureInvariant);

    public static IEnumerable<object[]> EnglishRules() => new[]
    {
        new object[] { "Preferences ▸", @"Preferences ▸" },
        new object[] { "Live Preview", @"Live Preview" },
        new object[] { "Mainline Map", @"Mainline Map" },
        new object[] { "UK spelling", @"(?i)\bnormalis|colour|\bcentre|centimetre|kilometre|\bgrey|cancelled|behaviour|neighbour|analys(e|es|ed|ing)\b" },
        new object[] { "three dots", @"\.\.\." },
        new object[] { "(s) plural", @"\(s\)" },
        new object[] { "build for compile", @"(?i)\b(re)?build(s|ing)?\b(?! target)" },
    };

    [Theory]
    [MemberData(nameof(EnglishRules))]
    public void English_values_follow_the_terminology(string rule, string pattern)
    {
        var re = new Regex(pattern, RegexOptions.CultureInvariant);
        // Not the compile command: ThIDE's own build switches, the Relational Map / graph rebuilds,
        // the About blurb and the hook variable list.
        string[] buildAllowed = { "Pref_EditorFeaturesHelp", "Status_BuildingGraph", "RelMap_RefreshTip", "Pref_SurveyAnalyticsHelp", "About_Blurb" };
        var bad = En.Where(kv => !IsLibraryTemplate(kv.Key))
            .Where(kv => !(rule == "build for compile" && buildAllowed.Contains(kv.Key)))
            .Where(kv => re.IsMatch(TherionWords.Replace(kv.Value, "")))
            .Select(kv => $"{kv.Key} = {kv.Value}")
            .ToList();
        Assert.True(bad.Count == 0, $"{rule}:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Settings_messages_name_the_Settings_window_and_its_real_pages()
    {
        Assert.Equal("Blender was not found. Set its path in Settings ▸ External Tools.", En["Blender_Fail_NotFound"]);
        Assert.Equal("Enable the embedded 3D model viewer in Settings ▸ Visualization.", En["Notif_3DDisabledMsg"]);
        Assert.Contains("Settings ▸ Extensions", En["Trust_Message"]);
        // The Settings menu keeps its Preferences… item.
        Assert.Equal("_Preferences…", En["Menu_Settings_Preferences"]);
    }

    [Fact]
    public void Overview_and_Workspace_Explorer_are_named_apart()
    {
        Assert.Equal("Toggle Overview (Dashboard / Surveys / Audit)", En["Cmd_ToggleProject"]);
        Assert.Equal("Toggle Workspace Explorer", En["Cmd_ToggleWorkspace"]);
        Assert.Equal("Workspace Explorer", En["Menu_View_Workspace"]);
        Assert.Equal(En["Tool_WorkspaceExplorer"], En["Menu_View_Workspace"]);
        Assert.Equal(RoT["Tool_WorkspaceExplorer"], RoT["Menu_View_Workspace"]);
    }

    [Fact]
    public void Menu_items_are_Title_Case()
    {
        string[] small = { "a", "an", "the", "and", "or", "to", "in", "on", "of", "for", "as", "at", "by", "from", "with", "into", "via" };
        // Therion command and file names stay as Therion writes them.
        string[] verbatim = { "thconfig", "dump3d", "extend", "fix", "lowercase", "th", "asc", "d" };
        var bad = En.Where(kv => kv.Key.StartsWith("Menu_", StringComparison.Ordinal))
            .Where(kv => Regex.Matches(kv.Value.Replace("_", ""), @"\p{L}[\p{L}'-]*").Skip(1)
                .Any(m => char.IsLower(m.Value[0]) && !small.Contains(m.Value) && !verbatim.Contains(m.Value)))
            .Select(kv => $"{kv.Key} = {kv.Value}")
            .ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Romanian_Cancel_is_Renunta_and_Anuleaza_is_only_Undo()
    {
        Assert.Equal("Renunță", RoT["Common_Cancel"]);
        Assert.Equal("Anulează", RoT["Common_Undo"]);
        var bad = RoT.Where(kv => kv.Value.Contains("Anulează", StringComparison.Ordinal)
                                  && !En[kv.Key].Contains("undo", StringComparison.OrdinalIgnoreCase))
            .Select(kv => $"{kv.Key} = {kv.Value} (EN {En[kv.Key]})")
            .ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Romanian_Workspace_is_spatiu_de_lucru()
    {
        var bad = En.Where(kv => kv.Value.Contains("workspace", StringComparison.OrdinalIgnoreCase)
                                 && !kv.Value.Contains("project", StringComparison.OrdinalIgnoreCase)
                                 && !kv.Value.Contains("${workspaceRoot}", StringComparison.Ordinal))
            .Where(kv => RoT[kv.Key].Contains("proiect", StringComparison.OrdinalIgnoreCase))
            .Select(kv => $"{kv.Key} = {RoT[kv.Key]} (EN {kv.Value})")
            .ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Romanian_sentences_use_the_formal_plural()
    {
        // Second-person singular forms that only occur in sentences addressed to the user.
        var informal = new Regex(@"\b(poți|vei|Rulezi|cunoști|Răspunde da|ai încredere|tale|tău|Apasă|Lasă|Dă)\b|(corectează|salvează|reîncarcă|scrie)-(l|le|o)\b",
            RegexOptions.CultureInvariant);
        var bad = RoT.Where(kv => informal.IsMatch(kv.Value)).Select(kv => $"{kv.Key} = {kv.Value}").ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void Plural_sets_have_all_three_forms_in_both_languages()
    {
        foreach (var key in En.Keys.Where(k => k.EndsWith("_One", StringComparison.Ordinal)))
        {
            var stem = key[..^4];
            foreach (var form in new[] { "_One", "_Few", "_Many" })
            {
                Assert.True(En.ContainsKey(stem + form), stem + form);
                Assert.True(RoT.ContainsKey(stem + form), stem + form);
            }
            Assert.Contains("{0}", En[stem + "_One"]);
        }
    }

    [Fact]
    public void Counts_take_the_singular_or_the_plural()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            Assert.Equal("Renamed 7 → 77 in 1 file.", Tr.Plural("Rename_Done", 1, "7", "77"));
            Assert.Equal("Renamed 7 → 77 in 3 files.", Tr.Plural("Rename_Done", 3, "7", "77"));
            Assert.Equal("Replaced 5 occurrences in 3 files.",
                string.Format(Tr.Get("Replace_Done"), Tr.Plural("Rename_Occurrences", 5), Tr.Plural("Rename_Files", 3)));
            Assert.Equal("Replaced 1 occurrence in 1 file.",
                string.Format(Tr.Get("Replace_Done"), Tr.Plural("Rename_Occurrences", 1), Tr.Plural("Rename_Files", 1)));
            Assert.Equal("1 file would change. Untick a file to leave it out.", EditPreviewViewModel.ChangeSummary("Normalize_Summary", 1, 0));
            Assert.Equal("4 files would change. 1 file is left alone because it does not parse cleanly (fix it first). Untick a file to leave it out.",
                EditPreviewViewModel.ChangeSummary("Normalize_Summary", 4, 1));
            Assert.Equal("Undid Rename in 1 file: a.th", Tr.Plural("WsUndo_Done", 1, "Rename", "a.th"));
        });
        UiLanguageCollection.In(Ro, () =>
        {
            Assert.Equal("S-a redenumit 7 → 77 în 1 fișier.", Tr.Plural("Rename_Done", 1, "7", "77"));
            Assert.Equal("S-a redenumit 7 → 77 în 3 fișiere.", Tr.Plural("Rename_Done", 3, "7", "77"));
            Assert.Equal("S-a redenumit 7 → 77 în 20 de fișiere.", Tr.Plural("Rename_Done", 20, "7", "77"));
            Assert.Equal("1 fișier s-ar schimba. Debifați un fișier pentru a-l omite.", EditPreviewViewModel.ChangeSummary("Normalize_Summary", 1, 0));
            Assert.Equal("3 segmente, 1 viză radială",
                string.Format(Tr.Get("PasteShots_Summary"), Tr.Plural("Count_Legs", 3), Tr.Plural("Count_Splays", 1)));
        });
    }

    [Fact]
    public void A_count_detail_of_a_shot_issue_takes_the_singular()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            Assert.Equal("1 extra column ignored.", PasteShotsViewModel.Describe(new ShotIssue(0, ShotIssueKind.ExtraColumns, "1")));
            Assert.Equal("2 extra columns ignored.", PasteShotsViewModel.Describe(new ShotIssue(0, ShotIssueKind.ExtraColumns, "2")));
        });
        UiLanguageCollection.In(Ro, () =>
            Assert.Equal("1 coloană în plus ignorată.", PasteShotsViewModel.Describe(new ShotIssue(0, ShotIssueKind.ExtraColumns, "1"))));
    }

    [Fact]
    public void Every_diagnostic_explanation_has_a_Romanian_text_and_the_English_one_matches_the_library()
    {
        foreach (var code in DiagnosticExplanations.Codes)
        {
            var key = "Diag_Explain_" + code;
            Assert.True(En.TryGetValue(key, out var en), key);
            Assert.Equal(DiagnosticExplanations.For(code)!.Summary, en);
            Assert.NotEqual(en, RoT[key]);
        }
    }

    [Fact]
    public void The_diagnostics_panel_shows_severity_and_explanation_in_Romanian()
    {
        UiLanguageCollection.In(Ro, () =>
        {
            var vm = new DiagnosticsViewModel();
            vm.Load(new[] { Diagnostic.Create("TH_SEM_006", DiagnosticSeverity.Warning, "x", SourceSpan.None) });
            var row = Assert.Single(vm.Rows);
            Assert.Equal("Avertisment", row.SeverityText);
            vm.Selected = row;
            Assert.Equal(RoT["Diag_Explain_TH_SEM_006"], vm.SelectedExplanation);
        });
    }

    [Fact]
    public void The_explorer_sort_label_follows_the_language()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            var vm = new WorkspaceExplorerViewModel();
            vm.SortMode = WorkspaceSortMode.Modified;
            Assert.Equal("Date Modified", vm.SortModeLabel);
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            Tr.Culture = Ro;
            LocProxy.Instance.Invalidate();

            Assert.Contains(nameof(WorkspaceExplorerViewModel.SortModeLabel), raised);
            Assert.Equal("Data modificării", vm.SortModeLabel);
        });
    }

    [Fact]
    public void Look_in_and_Group_by_options_have_labels_in_both_languages()
    {
        var ids = new SearchViewModel().Scopes.Select(s => "Opt_Scope_" + s)
            .Concat(new MeasurementsViewModel().GroupByOptions.Select(s => "Opt_GroupBy_" + s))
            .Concat(new MeasurementsViewModel().StationsGroupByOptions.Select(s => "Opt_GroupBy_" + s))
            .Concat(new[] { ReplaceInFilesViewModel.ScopeProject, ReplaceInFilesViewModel.ScopeDirectory, ReplaceInFilesViewModel.ScopeOpenFiles }
                .Select(s => "Opt_Scope_" + s));
        foreach (var key in ids)
        {
            Assert.True(En.ContainsKey(key), key);
            Assert.True(RoT.ContainsKey(key), key);
        }
        UiLanguageCollection.In(Ro, () =>
        {
            Assert.Equal("Proiect (thconfig-ul activ)", LocKeyConverter.Label("Opt_Scope_", ReplaceInFilesViewModel.ScopeProject));
            Assert.Equal("Fără grupare", LocKeyConverter.Label("Opt_GroupBy_", MeasurementsViewModel.GroupByNone));
            Assert.Equal("unknown", LocKeyConverter.Label("Opt_GroupBy_", "unknown"));
        });
    }

    [Fact]
    public void Station_kinds_and_yes_no_cells_are_not_English_enum_text()
    {
        foreach (var kind in Enum.GetNames<StationDeclarationKind>())
            Assert.True(RoT.ContainsKey("StationKind_" + kind), kind);
        UiLanguageCollection.In(Ro, () => Assert.Equal("Viză", Tr.Get("StationKind_" + StationDeclarationKind.Shot)));
        var check = BoolCheckConverter.Instance;
        Assert.Equal("✓", check.Convert(true, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, check.Convert(false, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Editor_feature_names_come_from_the_resx()
    {
        foreach (var info in EditorFeatureCatalog.All)
        {
            Assert.Equal(info.Title, En[$"Feat_{info.Feature}_Title"]);
            Assert.Equal(info.Description, En[$"Feat_{info.Feature}_Desc"]);
        }
        UiLanguageCollection.In(Ro, () =>
            Assert.Equal("Formatare document", new EditorFeatureToggle(EditorFeatureCatalog.Get(EditorFeature.FormatDocument), true).Title));
    }

    [Fact]
    public void The_assistant_data_policy_line_follows_a_language_switch()
    {
        UiLanguageCollection.In(CultureInfo.InvariantCulture, () =>
        {
            var settings = new AssistantViewModelTests.FakeSettings();
            settings.Save(settings.Current with { AssistantDataPolicyChosen = false });
            var vm = AssistantViewModelTests.NewVm(AssistantViewModelTests.AnswerOf("ok"), settings);
            settings.Save(settings.Current with { AssistantDataPolicy = Therion.Assistant.AssistantDataPolicy.HideContent, AssistantDataPolicyChosen = true });
            var note = Assert.IsType<NoteChatItem>(vm.Items.Last());
            Assert.StartsWith("Data policy: ", note.Text);

            Tr.Culture = Ro;
            LocProxy.Instance.Invalidate();

            Assert.StartsWith("Politica datelor: ", note.Text);
            Assert.Contains("Setări ▸ Extensii", note.Text);
        });
    }
}
