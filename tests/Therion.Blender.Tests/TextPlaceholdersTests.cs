// The placeholder for the cave's name in the texts written over the picture, and the texts the
// built-in presets and the user's own carry: how a text is filled in, where the name comes from,
// what becomes of a text the model has no name for, what each built-in preset writes, and that a
// preset without texts is the JSON, and has the hash, it had.

using Therion.Blender.Emit;
using Therion.Blender.Plan;
using Therion.Blender.Presets;
using Therion.Blender.Tests.Player;
using Therion.Blender.Tests.SceneProbe;
using Therion.CaveGeometry;

namespace Therion.Blender.Tests;

public class TextPlaceholdersTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-preset-texts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static readonly IReadOnlyDictionary<string, string> Named = new Dictionary<string, string> { ["cave"] = "Peștera Inventată" };

    private static readonly IReadOnlyDictionary<string, string> Unnamed = new Dictionary<string, string> { ["cave"] = "" };

    private static SceneMeta Meta(string? title, params SceneMetaSurvey[] surveys) => PlanFixtures.MediumMeta with
    {
        Source = PlanFixtures.MediumMeta.Source with { Title = title, Path = "/invented/out/cave.3d" },
        Surveys = surveys,
    };

    // ---- filling in ----

    [Fact]
    public void Fill_ReplacesWhatItKnows_InOnePass_AndLeavesEveryOtherBraceAlone()
    {
        Assert.Equal("Peștera Inventată", TextPlaceholders.Fill("{cave}", Named));
        Assert.Equal("Peștera Inventată, 2026 — Peștera Inventată", TextPlaceholders.Fill("{cave}, 2026 — {cave}", Named));
        Assert.Equal("{Cave} {cave } {} {year} } {", TextPlaceholders.Fill("{Cave} {cave } {} {year} } {", Named));
        Assert.Equal("{Peștera Inventată}", TextPlaceholders.Fill("{{cave}}", Named));
        Assert.Equal("a {b Peștera Inventată", TextPlaceholders.Fill("a {b {cave}", Named));
        Assert.Equal("", TextPlaceholders.Fill("", Named));
        // What a value holds is not looked at again.
        var braces = new Dictionary<string, string> { ["cave"] = "{cave} {year}", ["year"] = "2026" };
        Assert.Equal("{cave} {year} 2026", TextPlaceholders.Fill("{cave} {year}", braces));

        Assert.True(TextPlaceholders.HasAny("Explorarea din {cave}"));
        Assert.False(TextPlaceholders.HasAny("Explorarea din {peșteră}"));
        Assert.False(TextPlaceholders.HasAny(null));
        Assert.Equal(["{cave}"], TextPlaceholders.Known);
    }

    [Fact]
    public void TheCavesName_IsTheModelsTitle_OrTheTitleAtTheRootOfItsSurveys()
    {
        var root = new SceneMetaSurvey(0, 0, "", "Sistemul Inventat");
        var bare = new SceneMetaSurvey(0, 0, "", null);

        // A .3d file has a title of its own.
        Assert.Equal("Peștera Inventată", TextPlaceholders.CaveName(Meta("  Peștera Inventată ")));
        Assert.Equal("Peștera Inventată", TextPlaceholders.CaveName(Meta("Peștera Inventată", root)));
        // A .lox file has a tree of surveys: its root's title, or that of the one survey under a root without one.
        Assert.Equal("Sistemul Inventat", TextPlaceholders.CaveName(Meta(null, root, new SceneMetaSurvey(1, 0, "upper", "Etajul superior"))));
        Assert.Equal("Etajul superior", TextPlaceholders.CaveName(Meta(null, bare, new SceneMetaSurvey(1, 0, "upper", "Etajul superior"), new SceneMetaSurvey(2, 1, "a", "A"))));
        // Two caves under a root without a title have no one name.
        Assert.Equal("", TextPlaceholders.CaveName(Meta(null, bare, new SceneMetaSurvey(1, 0, "a", "A"), new SceneMetaSurvey(2, 0, "b", "B"))));
        Assert.Equal("", TextPlaceholders.CaveName(Meta(" ", bare)));
        // A title that is only a file's name is no name: that of the model's own file, with its
        // extension or without, in any case, or of any other model file. The surveys are asked then.
        Assert.Equal("", TextPlaceholders.CaveName(Meta("cave.3d")));
        Assert.Equal("", TextPlaceholders.CaveName(Meta("Cave")));
        Assert.Equal("", TextPlaceholders.CaveName(Meta("export-2026.3D")));
        Assert.Equal("", TextPlaceholders.CaveName(Meta("walls.lox")));
        Assert.Equal("Sistemul Inventat", TextPlaceholders.CaveName(Meta("cave", root)));
        Assert.Equal("Cave of the Winds", TextPlaceholders.CaveName(Meta("Cave of the Winds")));
        Assert.Equal("cave", TextPlaceholders.CaveName(Meta("cave") with { Source = PlanFixtures.MediumMeta.Source with { Title = "cave", Path = null } }));
        Assert.Equal("", TextPlaceholders.CaveName(Meta(null)));
        Assert.Equal("", TextPlaceholders.CaveName(null));
        // The invented caves are .lox models, named at their root.
        Assert.Equal("Invented Medium Cave", TextPlaceholders.CaveName(PlanFixtures.MediumMeta));
        Assert.Equal("Invented Medium Cave", TextPlaceholders.ValuesOf(PlanFixtures.MediumMeta)["cave"]);
    }

    [Fact]
    public void EveryTextOfTheOverlays_IsFilledIn_AndOneThatIsLeftBlank_IsLeftOut()
    {
        var overlays = new OverlaySpec
        {
            Title = "{cave}", Subtitle = "ridicarea {cave}", Credits = "{cave}", Corner = " {cave} ", ScaleBar = true,
            EndCard = new EndCardSpec { Text = "{cave}\n2026", Subtitle = "{cave}", Seconds = 2 },
            Captions = [new CaptionSpec { Text = "Intrarea în {cave}", Start = 0, End = 1 }, new CaptionSpec { Text = "{cave}", Start = 1, End = 2 }],
            CornerCaptions = [new CaptionSpec { Text = "{cave}", Start = 0, End = 1 }, new CaptionSpec { Text = "etajul 2", Start = 1, End = 2 }],
        };

        var named = TextPlaceholders.Resolve(overlays, Named);
        Assert.Equal(
            ("Peștera Inventată", "ridicarea Peștera Inventată", "Peștera Inventată", "Peștera Inventată"),
            (named.Title, named.Subtitle, named.Credits, named.Corner));
        Assert.Equal(new EndCardSpec { Text = "Peștera Inventată\n2026", Subtitle = "Peștera Inventată", Seconds = 2 }, named.EndCard);
        Assert.Equal(["Intrarea în Peștera Inventată", "Peștera Inventată"], named.Captions.Select(c => c.Text));
        Assert.Equal(["Peștera Inventată", "etajul 2"], named.CornerCaptions.Select(c => c.Text));
        Assert.True(named.ScaleBar);

        // Over a model without a name: what was only the name is gone, the rest stands without it.
        var unnamed = TextPlaceholders.Resolve(overlays, Unnamed);
        Assert.Equal((null, "ridicarea", null, null), (unnamed.Title, unnamed.Subtitle, unnamed.Credits, unnamed.Corner));
        Assert.Equal(new EndCardSpec { Text = "\n2026", Subtitle = null, Seconds = 2 }, unnamed.EndCard);
        Assert.Equal([new CaptionSpec { Text = "Intrarea în ", Start = 0, End = 1 }], unnamed.Captions);
        Assert.Equal([new CaptionSpec { Text = "etajul 2", Start = 1, End = 2 }], unnamed.CornerCaptions);
        // An end card that was only the name is no card: the movie is no longer for it.
        Assert.Null(TextPlaceholders.Resolve(new OverlaySpec { EndCard = new EndCardSpec { Text = "{cave}" } }, Unnamed).EndCard);
    }

    [Fact]
    public void TextsWithoutAPlaceholder_AreTheSameObject_AndSoIsTheirSpec()
    {
        var overlays = new OverlaySpec { Title = "Peștera {Inventată}", Credits = "echipa", Captions = [new CaptionSpec { Text = "{}", End = 1 }] };
        var spec = new SceneSpec { Labels = new LabelsSpec { Overlays = overlays } };

        Assert.Same(overlays, TextPlaceholders.Resolve(overlays, Named));
        Assert.Same(spec, TextPlaceholders.Resolve(spec, PlanFixtures.MediumMeta));
        Assert.Same(spec, TextPlaceholders.Resolve(spec, null));
        // A blank title that held no placeholder is not this code's to remove.
        Assert.Equal(" ", TextPlaceholders.Resolve(new OverlaySpec { Title = " ", Credits = "{cave}" }, Named).Title);
    }

    // ---- lowering ----

    [Fact]
    public void FromSpec_WritesTheCavesNameWhereTheSpecHasThePlaceholder()
    {
        var spec = PlanFixtures.Preset("Orbit showcase") with
        {
            Labels = new LabelsSpec
            {
                Overlays = new OverlaySpec
                {
                    Title = "{cave}", Credits = "ridicare: echipa {cave}", EndCard = new EndCardSpec { Text = "{cave}", Seconds = 1 },
                    Captions = [new CaptionSpec { Text = "Sala Mare, {cave}", Start = 1, End = 3 }],
                },
            },
        };

        var plan = PlanFixtures.PlanOf(spec);

        // (A card is a box and the text on it: two items of the role.)
        var texts = plan.Texts!.Items.Where(i => !string.IsNullOrEmpty(i.Text)).ToLookup(i => i.Role!, i => i.Text!);
        Assert.Equal(["Invented Medium Cave"], texts["title"]);
        Assert.Equal(["ridicare: echipa Invented Medium Cave"], texts["credits"]);
        Assert.Equal(["Invented Medium Cave"], texts["end-card"]);
        Assert.Equal(["Sala Mare, Invented Medium Cave"], texts["caption"]);
        Assert.DoesNotContain("{cave}", ScenePlanSerializer.Write(plan));
        Assert.Empty(ScenePlanValidator.Validate(plan));

        // Without a model's metadata there is no name: the title, which was only that, is not written.
        var bare = ScenePlanBuilder.FromSpec(PlanFixtures.WithPaths(spec), PlanFixtures.MediumFraming);
        Assert.DoesNotContain(bare.Texts!.Items, i => i.Role is "title" or "end-card");
        Assert.Equal("ridicare: echipa", bare.Texts.Items.Single(i => i.Role == "credits").Text);
    }

    // ---- the built-in presets ----

    [Fact]
    public void EveryBuiltInPreset_NamesTheCave_AtTheTopOrOnACardItOpensOn_InNoLanguage()
    {
        var titles = BuiltInPresets.All.ToDictionary(p => p.Name, p => (p.Spec.Labels.Overlays.Title, p.Spec.Labels.Overlays.TitleSeconds));

        Assert.Equal(
            new Dictionary<string, (string?, double?)>
            {
                ["Orbit showcase"] = ("{cave}", null),
                ["Helix descent"] = ("{cave}", 3.0),
                ["Full flythrough"] = ("{cave}", 3.0),
                ["Map reveal"] = ("{cave}", null),
                ["Documentation stills"] = ("{cave}", null),
            },
            titles);
        Assert.Equal(3.0, BuiltInPresets.TitleCardSeconds);
        // Nothing else is written: no words of any one language, and nothing the panel cannot change.
        Assert.All(BuiltInPresets.All, p =>
        {
            var o = p.Spec.Labels.Overlays;
            Assert.Equal(new OverlaySpec { Title = o.Title, TitleSeconds = o.TitleSeconds }, o);
            Assert.Empty(SceneSpecValidator.Validate(p.ToRenderSpec(new SourceSpec { PlyPath = "job/model.ply" }, "renders", "cave")));
        });
    }

    [Fact]
    public void ABuiltInPresetsPlan_HasTheCavesName_AndOverACaveWithoutOne_NoTextsAndNoSecondStage()
    {
        foreach (var preset in BuiltInPresets.All)
        {
            var plan = PlanFixtures.PlanOf(preset.Spec);
            var title = plan.Texts!.Items.Single(i => i.Role == "title");
            Assert.Equal("Invented Medium Cave", title.Text);
            Assert.Contains(ScenePlan.TextsFeature, plan.Features);
            Assert.Empty(ScenePlanValidator.Validate(plan));
            // A card stands in the middle and goes; a title at the top stays.
            bool card = preset.Spec.Labels.Overlays.TitleSeconds is not null && preset.Spec.Output.Kind != OutputKind.Still;
            Assert.Equal(card ? 3.0 + ScreenTextPlanner.TitleFadeSeconds : null, title.End);

            var unnamed = ScenePlanBuilder.FromSpec(PlanFixtures.WithPaths(preset.Spec), PlanFixtures.MediumFraming, Meta(null));
            Assert.Null(unnamed.Texts);
            Assert.False(unnamed.HasTexts);
        }
    }

    [Fact]
    public void MapReveal_CountsTheYears_WhenTheCaveIsRevealedByDate()
    {
        var preset = BuiltInPresets.MapReveal().Spec;
        Assert.Equal((RevealBy.Distance, true), (preset.Reveal!.By, preset.Reveal.Counter));

        // As shipped, by distance: the cave's name, and no years to count.
        Assert.Equal(["title"], PlanFixtures.PlanOf(preset).Texts!.Items.Select(i => i.Role));

        // Switched to the dates of the survey, on a cave surveyed in 1987, 1994 and 2003.
        var byDate = preset with { Reveal = preset.Reveal with { By = RevealBy.Date } };
        var geometry = GeometryStage.Build(RevealFixtures.MediumByExpedition(), RevealPlanner.GeometryFor(byDate));
        var plan = ScenePlanBuilder.FromSpec(PlanFixtures.WithPaths(byDate), CameraFraming.FromGeometry(geometry), SceneMeta.Build(geometry));

        Assert.Equal(["title", RevealPlanner.CounterRole], plan.Texts!.Items.Select(i => i.Role));
        var counter = plan.Texts.Items[^1];
        Assert.Equal(("{}", 1987.0, 2003.0), (counter.Text, counter.Counter!.Min, counter.Counter.Max));
        Assert.Equal("Invented Medium Cave", plan.Texts.Items[0].Text);
        Assert.Empty(ScenePlanValidator.Validate(plan));
    }

    [Fact]
    public void TheScriptGenerator_SaysWhichOfAPresetsTextsItDoesNotDraw()
    {
        // It draws a title in the scene; a card that goes after a time is the player's.
        Assert.Empty(SceneBuilders.NotDrawnByTheGenerator(BuiltInPresets.OrbitShowcase().Spec));
        Assert.Equal(["labels.overlays.titleSeconds"], SceneBuilders.NotDrawnByTheGenerator(BuiltInPresets.HelixDescent().Spec));
        Assert.Equal(["labels.overlays.titleSeconds", "reveal"], SceneBuilders.NotDrawnByTheGenerator(BuiltInPresets.MapReveal().Spec with
        {
            Labels = BuiltInPresets.HelixDescent().Spec.Labels,
        }));
        Assert.All(BuiltInPresets.All, p => Assert.Equal(SceneBuilder.Player, SceneBuilders.Choose(p.Spec).Builder));
    }

    // ---- hashes, and the files of presets ----

    [Fact]
    public void ASpecWithoutTheNewFields_HasTheHashItHadBeforeTheyExisted()
    {
        // Computed with the release before this one, from the same specs.
        Assert.Equal("3ff22ee029487a6abe06b7d6c53a213c34b3c19ada54c8995c5d745c780808c7", SceneSpecSerializer.ComputeHash(new SceneSpec()));
        Assert.Equal(
            "8b6754557d152eb5951402f4573d3572f5cfd94395b26839fd435f6a2fa95363",
            SceneSpecSerializer.ComputeHash(new SceneSpec { Lighting = new LightingSpec { Rig = LightingRig.HdriFile, HdriPath = "/hdris/cave.exr", Strength = 2.5 } }));
        // The built-in presets without the texts they have now are the presets they were.
        var before = new Dictionary<string, string>
        {
            ["Orbit showcase"] = "bbbb1f62a6e380e67c15f075e9f71efe1babaad49db1d33124732d296e793488",
            ["Helix descent"] = "73ee5b8347dcb0f917ff25981ba8fea51c49fb5643679dcd61bb9694f27e112e",
            ["Full flythrough"] = "631c66e8e715478f2b2ecb083d68f40f954276868da8b9bca7c9e998675b941f",
            ["Map reveal"] = "23713e6ccc2cb65ba28384507cb9658ca8739559873bfaf52d9dd6ddd84f8158",
            ["Documentation stills"] = "166baa5d0396f424ff55ba641b3a00421c8f4ce2ccd7134fdf1ccebfb1763c16",
        };
        Assert.All(BuiltInPresets.All, p =>
        {
            var plain = p.Spec with { Labels = p.Spec.Labels with { Overlays = new OverlaySpec() } };
            Assert.Equal(before[p.Name], SceneSpecSerializer.ComputeHash(plain));
            Assert.NotEqual(before[p.Name], SceneSpecSerializer.ComputeHash(p.Spec));
        });
    }

    [Fact]
    public void AUsersPreset_KeepsItsTexts_ThroughTheStore_AndOneWrittenBeforeTheyExisted_ReadsAsItWas()
    {
        var overlays = new OverlaySpec
        {
            Title = "{cave}", TitleSeconds = 2, Credits = "ridicare: Clubul Inventat, 2026",
            EndCard = new EndCardSpec { Text = "Mulțumim\nechipei", Seconds = 3 },
            Captions = [new CaptionSpec { Text = "Sala Mare", Start = 1, End = 3.5 }, new CaptionSpec { Text = "Sifonul: capăt", Start = 4, End = 6 }],
        };
        var preset = new RenderPreset
        {
            Name = "Prezentare cu texte",
            Spec = BuiltInPresets.HelixDescent().Spec with { Labels = new LabelsSpec { Overlays = overlays } },
        };
        var store = new PresetStore(_dir);

        string path = store.Save(preset);
        var loaded = Assert.Single(store.Load());

        var back = loaded.Spec.Labels.Overlays;
        Assert.Equal((overlays.Title, overlays.TitleSeconds, overlays.Credits, overlays.EndCard), (back.Title, back.TitleSeconds, back.Credits, back.EndCard));
        Assert.Equal(overlays.Captions, back.Captions);
        Assert.Equal(SceneSpecSerializer.ComputeHash(preset.Spec), SceneSpecSerializer.ComputeHash(loaded.Spec));
        Assert.Contains("\"text\": \"Sifonul: capăt\"", File.ReadAllText(path));

        // A preset file from before the texts, the reveal and the turn of an HDRI image: none of
        // their keys, and it reads as the preset it was, with the hash its spec had.
        string old = """
            {
              "version": 1,
              "name": "De anul trecut",
              "builtIn": false,
              "spec": {
                "version": 1, "seed": 7,
                "lighting": { "rig": "HdriFile", "strength": 1.5, "hdriPath": "/hdris/cave.exr" },
                "camera": { "template": "Orbit", "focalLength": 35, "autoFramePadding": 1.15 },
                "labels": { "overlays": { "title": "Peștera Inventată", "scaleBar": true, "northArrow": false, "depthLegend": false } },
                "animation": { "fps": 30, "durationSeconds": 12 },
                "output": { "kind": "Video", "container": "Mp4", "width": 1920, "height": 1080, "outputDirectory": "", "baseName": "cave-render" }
              }
            }
            """;
        File.WriteAllText(Path.Combine(_dir, "de-anul-trecut" + PresetStore.Extension), old);
        var read = store.Load().Single(p => p.Name == "De anul trecut");
        var texts = read.Spec.Labels.Overlays;
        Assert.Equal(("Peștera Inventată", true, null, null, 0), (texts.Title, texts.ScaleBar, texts.Credits, texts.EndCard, texts.Captions.Count));
        Assert.Equal((LightingRig.HdriFile, 1.5, 0.0), (read.Spec.Lighting.Rig, read.Spec.Lighting.Strength, read.Spec.Lighting.HdriRotation));
        var same = new SceneSpec
        {
            Seed = 7,
            Lighting = new LightingSpec { Rig = LightingRig.HdriFile, Strength = 1.5, HdriPath = "/hdris/cave.exr" },
            Camera = new CameraSpec { Template = CameraTemplate.Orbit },
            Labels = new LabelsSpec { Overlays = new OverlaySpec { Title = "Peștera Inventată", ScaleBar = true } },
            Animation = new AnimationSpec { Fps = 30, DurationSeconds = 12 },
        };
        Assert.Equal(SceneSpecSerializer.ComputeHash(same), SceneSpecSerializer.ComputeHash(read.Spec));
        // Saved again, it is a file an older build still reads: nothing in it but what a spec has.
        store.Save(read);
        Assert.Equal(SceneSpecSerializer.ComputeHash(same), SceneSpecSerializer.ComputeHash(store.Load().Single(p => p.Name == "De anul trecut").Spec));
    }
}
