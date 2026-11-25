// A plan's annotations without Blender: how they are written and read, what the validator, the
// reader and the schema refuse, and how a change finds one. In Blender they are in
// PlayerAnnotationsBlenderTests.

using Therion.Blender.Plan;
using Therion.Blender.Player;
using Therion.Cave3D;

namespace Therion.Blender.Tests.Player;

public class ScenePlanAnnotationsTests
{
    private static ScenePlan Full => PlanFixtures.Full();

    private static string FullJson => ScenePlanSerializer.Write(Full);

    private static IEnumerable<string> Paths(ScenePlan plan) => ScenePlanValidator.Validate(plan).Select(e => e.Path);

    /// <summary>The full plan with one of its annotations changed: 0 the label, 1 the callout,
    /// 2 the marker, 3 the photo that faces the camera, 4 the photo that is fixed.</summary>
    private static ScenePlan With(int index, Func<PlanAnnotation, PlanAnnotation> change)
        => Full with { Annotations = [.. Full.Annotations!.Select((a, i) => i == index ? change(a) : a)] };

    private const int Label = 0, Callout = 1, Marker = 2, Photo = 3, Card = 4;

    // ---- written and read ----

    [Fact]
    public void Annotations_AreWrittenAfterTheLabels_EachWithWhatItsKindHas_AndReadBackTheSame()
    {
        Assert.Contains(
            "  \"annotations\": [\n" +
            "    {\n" +
            "      \"id\": \"hall\",\n" +
            "      \"role\": \"place-name\",\n" +
            "      \"kind\": \"label\",\n" +
            "      \"at\": [12, -3, 1.5],\n" +
            "      \"model\": \"CaveModel\",\n" +
            "      \"text\": \"Sala \\\"Mare\\\"\",\n" +
            "      \"size\": 1.4,\n" +
            "      \"color\": [0.9, 0.95, 1],\n" +
            "      \"strength\": 2.5\n" +
            "    },\n", FullJson);
        Assert.Contains(
            "    {\n" +
            "      \"id\": \"notice\",\n" +
            "      \"role\": \"notice-board\",\n" +
            "      \"kind\": \"photo\",\n" +
            "      \"at\": [1.5, 2, 3],\n" +
            "      \"image\": \"photos/notice.png\",\n" +
            "      \"width\": 2,\n" +
            "      \"facing\": \"fixed\",\n" +
            "      \"normal\": [0, -1, 0.2],\n" +
            "      \"up\": [0, 0, 1]\n" +
            "    }\n" +
            "  ],\n" +
            "  \"changes\": [\n", FullJson);
        Assert.True(FullJson.IndexOf("\"labels\": {", StringComparison.Ordinal) < FullJson.IndexOf("\"annotations\": [", StringComparison.Ordinal));

        var read = ScenePlanSerializer.Read(FullJson);

        Assert.Equal(Full.Annotations!, read.Annotations!);
        Assert.Null(read.Sections);
        Assert.Equal(FullJson, ScenePlanSerializer.Write(read));
    }

    [Fact]
    public void APlanWithoutAnnotations_WritesNoSuchSection_AndTheEmptyListItsNameWasReservedWith_StillReads()
    {
        var bare = Full with { Annotations = null, Changes = [.. Full.Changes!.Where(c => c.Target?.Id != "photo-hall")] };
        Assert.DoesNotContain("\"annotations\":", ScenePlanSerializer.Write(bare));

        string reserved = ScenePlanSerializer.Write(bare).TrimEnd().TrimEnd('}') + ",\n  \"annotations\": []\n}\n";
        var plan = ScenePlanSerializer.Read(reserved);

        Assert.Empty(plan.Annotations!);
        Assert.Null(plan.Sections);
        // Nothing is annotated, so no module is needed for it.
        Assert.Empty(ScenePlanValidator.Validate(plan with { Features = Full.Features.Where(f => f != ScenePlan.AnnotationsFeature).ToList() }));
    }

    [Theory]
    [InlineData("\"radius\": 0.6,", "\"radius\": 0.6, \"pulse\": 2,", "annotations[2].pulse")]
    [InlineData("\"width\": 0.12,", "\"width\": 0.12, \"corner\": 0.1,", "annotations[3].frame.corner")]
    [InlineData("\"facing\": \"fixed\",", "\"facing\": \"fixed\", \"window\": { \"from\": 1 },", "annotations[4].window")]
    public void AKeyOfAnAnnotationThisBuildDoesNotKnow_IsRefusedByItsPath(string original, string changed, string path)
    {
        Assert.Contains(original, FullJson);

        var ex = Assert.Throws<ScenePlanFormatException>(() => ScenePlanSerializer.Read(FullJson.Replace(original, changed)));

        Assert.Equal($"{path} is not known to this build (scene plan version 1); a newer build may be needed.", ex.Message);
    }

    // ---- the schema ----

    [Theory]
    [InlineData("\"kind\": \"marker\"", "\"kind\": \"arrow\"", "$.annotations[2].kind: \"arrow\" is not one of")]
    [InlineData("\"radius\": 0.6", "\"radius\": 0", "$.annotations[2].radius: not above 0")]
    [InlineData("\"at\": [-4, 0, 0.25],\n      \"radius\"", "\"radius\"", "$.annotations[2]: 'at' is missing")]
    [InlineData("\"id\": \"dig\",", "", "$.annotations[2]: 'id' is missing")]
    [InlineData("\"offset\": [2, 0, 6]", "\"offset\": [2, 0]", "$.annotations[1].offset: fewer than 3 items")]
    [InlineData("\"image\": \"photos/hall.jpg\"", "\"image\": \"photos/hall.gif\"", "$.annotations[3].image: 'photos/hall.gif' does not match")]
    [InlineData("\"image\": \"photos/hall.jpg\"", "\"image\": \"../hall.jpg\"", "$.annotations[3].image: '../hall.jpg' does not match")]
    [InlineData("\"facing\": \"fixed\"", "\"facing\": \"north\"", "$.annotations[4].facing: \"north\" is not one of")]
    [InlineData("\"role\": \"notice-board\"", "\"role\": \"Notice Board\"", "$.annotations[4].role: 'Notice Board' does not match")]
    [InlineData("\"width\": 0.12,", "", "$.annotations[3].frame: 'width' is missing")]
    [InlineData("\"radius\": 0.6,", "\"radius\": 0.6, \"pulse\": 2,", "$.annotations[2]: 'pulse' is not a known property")]
    public void WhatTheFormatForbidsOfAnAnnotation_DoesNotConform(string original, string changed, string expected)
    {
        Assert.Contains(original, FullJson);

        var problems = JsonSchemaSubset.Validate(ScenePlanSchema.Text, FullJson.Replace(original, changed));

        Assert.Contains(problems, p => p.StartsWith(expected, StringComparison.Ordinal));
    }

    // ---- the validator ----

    [Fact]
    public void Annotations_NeedTheirModule_InItsPlace_AndIdsNothingElseInTheSceneHas()
    {
        Assert.Empty(Paths(Full));
        Assert.Equal(["annotations"], Paths(Full with { Features = Full.Features.Where(f => f != ScenePlan.AnnotationsFeature).ToList() }));
        var order = ScenePlan.PlayerFeatures.ToList();
        int module = order.IndexOf(ScenePlan.AnnotationsFeature);
        Assert.True(order.IndexOf("camera_track") < module && order.IndexOf("models") < module && order.IndexOf(ScenePlan.LinesFeature) < module);
        Assert.True(order.IndexOf(ScenePlan.LabelsFeature) < module && module < order.IndexOf(ScenePlan.ChangesFeature));
        Assert.Contains(PlayerFiles.JobFiles, f => f.Path == "thide_player/modules/annotations.py");

        // An annotation turns to the camera, rides the model it names, and is found by a change.
        string[] Moved(string before) => [.. Full.Features.Where(f => f != ScenePlan.AnnotationsFeature && f != before), ScenePlan.AnnotationsFeature, before];
        Assert.Equal(["features"], Paths(Full with { Features = [.. Moved("camera_track").Where(f => f != "output"), "output"] }).Distinct());
        Assert.Contains("features", Paths(Full with { Features = [ScenePlan.AnnotationsFeature, .. Full.Features.Where(f => f != ScenePlan.AnnotationsFeature)] }));
        var afterChanges = Full.Features.Where(f => f != ScenePlan.AnnotationsFeature && f != "output").Append(ScenePlan.AnnotationsFeature).Append("output").ToList();
        Assert.Equal(["features"], Paths(Full with { Features = afterChanges }));
        // The lines may be made later, as long as no annotation rides one.
        string[] linesLater = ["models", "camera_track", "annotations", "lines", "look", "textured_materials", "labels", "changes", "texts", "blend_file", "output"];
        Assert.Empty(Paths(Full with { Features = linesLater }));
        Assert.Equal(["features"], Paths(With(Label, a => a with { Model = "Legs" }) with { Features = linesLater }));

        Assert.Equal(["annotations[0].id"], Paths(With(Label, a => a with { Id = " " })));
        foreach (string taken in new[] { "sump", "CaveModel", "Legs", "stn:entrance", "here", "Key" })
            Assert.Equal("annotations[2].id", Paths(With(Marker, a => a with { Id = taken })).First());
        Assert.Equal(["annotations[0].role"], Paths(With(Label, a => a with { Role = "Place Name" })));
        Assert.Equal(["annotations[2].at"], Paths(With(Marker, a => a with { At = new CaveVector3(0, double.NaN, 0) })));
        Assert.Equal(["annotations[0].model"], Paths(With(Label, a => a with { Model = "Elsewhere" })));
        Assert.Empty(Paths(With(Label, a => a with { Model = "Legs" })));
        Assert.Equal(["annotations[2].kind"], Paths(With(Marker, a => a with { Kind = (PlanAnnotationKind)9 })));
        Assert.Equal(["annotations[1]"], Paths(Full with { Annotations = [Full.Annotations![0], null!, .. Full.Annotations.Skip(2)] }));
    }

    [Fact]
    public void ALabelAndACallout_NeedTheirTextAndItsSize_AndACalloutStandsAwayFromItsAnchor()
    {
        foreach (int index in new[] { Label, Callout })
        {
            Assert.Equal([$"annotations[{index}].text"], Paths(With(index, a => a with { Text = null })));
            Assert.Equal([$"annotations[{index}].text"], Paths(With(index, a => a with { Text = "" })));
            Assert.Equal([$"annotations[{index}].size"], Paths(With(index, a => a with { Size = null })));
            Assert.Equal([$"annotations[{index}].size"], Paths(With(index, a => a with { Size = 0 })));
            Assert.Equal([$"annotations[{index}].color"], Paths(With(index, a => a with { Color = new ColorRgb(1.5, 0, 0) })));
            Assert.Equal([$"annotations[{index}].strength"], Paths(With(index, a => a with { Strength = -1 })));
        }
        Assert.Equal(["annotations[1].offset"], Paths(With(Callout, a => a with { Offset = null })));
        Assert.Equal(["annotations[1].offset"], Paths(With(Callout, a => a with { Offset = CaveVector3.Zero })));
        Assert.Equal(["annotations[1].offset"], Paths(With(Callout, a => a with { Offset = new CaveVector3(0, 0, double.PositiveInfinity) })));
        Assert.Equal(["annotations[1].lineWidth"], Paths(With(Callout, a => a with { LineWidth = 0 })));
        Assert.Empty(Paths(With(Callout, a => a with { LineWidth = null })));
    }

    [Fact]
    public void AMarker_NeedsItsRadius()
    {
        Assert.Equal(["annotations[2].radius"], Paths(With(Marker, a => a with { Radius = null })));
        Assert.Equal(["annotations[2].radius"], Paths(With(Marker, a => a with { Radius = double.NaN })));
    }

    [Fact]
    public void APhoto_NeedsAnImageBesideThePlan_AWidth_AndAFixedOneTheWayItFaces()
    {
        foreach (string image in new[] { "/abs/hall.jpg", "../hall.jpg", "C:\\photos\\hall.jpg", "photos/hall.gif", "photos/hall", "" })
            Assert.Equal(["annotations[3].image"], Paths(With(Photo, a => a with { Image = image })));
        Assert.Equal(["annotations[3].image"], Paths(With(Photo, a => a with { Image = null })));
        Assert.Empty(Paths(With(Photo, a => a with { Image = "photos/Hall.JPEG" })));
        Assert.Equal(["annotations[3].width"], Paths(With(Photo, a => a with { Width = null })));
        Assert.Equal(["annotations[3].height"], Paths(With(Photo, a => a with { Height = -4 })));
        Assert.Empty(Paths(With(Photo, a => a with { Height = null })));
        Assert.Equal(["annotations[3].frame.width"], Paths(With(Photo, a => a with { Frame = new PlanPhotoFrame { Width = 0 } })));
        Assert.Equal(["annotations[3].frame.color"], Paths(With(Photo, a => a with { Frame = new PlanPhotoFrame { Color = new ColorRgb(0, 0, 2) } })));
        // A line to its anchor needs it to stand away from it.
        Assert.Equal(["annotations[3].lineWidth"], Paths(With(Photo, a => a with { Offset = null })));
        Assert.Empty(Paths(With(Photo, a => a with { Offset = null, LineWidth = null })));

        Assert.Equal(["annotations[4].facing"], Paths(With(Card, a => a with { Facing = (PlanFacing)7 })));
        Assert.Equal(["annotations[4].normal"], Paths(With(Card, a => a with { Normal = null })));
        Assert.Equal(["annotations[4].normal"], Paths(With(Card, a => a with { Normal = CaveVector3.Zero })));
        Assert.Equal(["annotations[4].up"], Paths(With(Card, a => a with { Up = new CaveVector3(0, -5, 1) })));   // along the normal
        Assert.Empty(Paths(With(Card, a => a with { Up = null })));
        // One that faces the camera has no direction of its own.
        Assert.Equal(["annotations[4].normal", "annotations[4].up"], Paths(With(Card, a => a with { Facing = PlanFacing.Camera })));
        Assert.Equal(["annotations[3].normal"], Paths(With(Photo, a => a with { Normal = new CaveVector3(1, 0, 0) })));
    }

    [Fact]
    public void WhatBelongsToAnotherKind_IsRefusedByName()
    {
        var errors = ScenePlanValidator.Validate(With(Label, a => a with { Radius = 2, Image = "photos/x.png", Offset = new CaveVector3(0, 0, 1) }));
        Assert.Equal(
            ["annotations[0].radius: A label has no 'radius'.", "annotations[0].offset: A label has no 'offset'.", "annotations[0].image: A label has no 'image'."],
            errors.Select(e => $"{e.Path}: {e.Message}"));
        Assert.Equal(["annotations[2].text"], Paths(With(Marker, a => a with { Text = "here" })));
        Assert.Equal(["annotations[2].frame"], Paths(With(Marker, a => a with { Frame = new PlanPhotoFrame() })));
        Assert.Equal(["annotations[3].color"], Paths(With(Photo, a => a with { Color = new ColorRgb(1, 0, 0) })));
        Assert.Equal(["annotations[1].facing"], Paths(With(Callout, a => a with { Facing = PlanFacing.Camera })));
    }

    // ---- a change finds an annotation ----

    [Fact]
    public void AChange_NamesAnAnnotationByItsId_OrByItsRole_WhichIsItsKindUnlessItSaysOtherwise()
    {
        PlanChange Hide(PlanChangeTarget target) => new() { Target = target, Visible = [new PlanVisibleKey(1, false, 0.2)] };
        ScenePlan Changed(params PlanChange[] changes) => Full with { Changes = changes };

        Assert.Empty(Paths(Changed(Hide(new PlanChangeTarget { Id = "sump" }))));
        Assert.Empty(Paths(Changed(Hide(new PlanChangeTarget { Role = "callout" }))));
        Assert.Empty(Paths(Changed(Hide(new PlanChangeTarget { Role = "photo" }), Hide(new PlanChangeTarget { Role = "notice-board" }))));
        Assert.Empty(Paths(Changed(Hide(new PlanChangeTarget { Role = "place-name" }))));
        Assert.Equal(["changes[0].target.id"], Paths(Changed(Hide(new PlanChangeTarget { Id = "no-such" }))));
        // An annotation fades, takes a tint and is more or less there: it has a material.
        Assert.Empty(Paths(Changed(new PlanChange
        {
            Target = new PlanChangeTarget { Id = "photo-hall" },
            Opacity = [new PlanNumberKey(0, 0.5)],
            Tint = [new PlanTintKey(0, new ColorRgb(1, 0, 0), 0.3)],
        })));
        // It is not stretched or moved: where it stands is its own section's.
        Assert.Equal(["changes[0].scale"], Paths(Changed(new PlanChange
        {
            Target = new PlanChangeTarget { Id = "dig" }, Scale = [new PlanVectorKey(0, new CaveVector3(2, 2, 2))],
        })));
        Assert.Equal(["changes[0].offset"], Paths(Changed(new PlanChange
        {
            Target = new PlanChangeTarget { Id = "dig" }, Offset = [new PlanVectorKey(0, new CaveVector3(0, 0, 2))],
        })));
        // Two changes do not show and hide the same one, by its id and by its role.
        Assert.Equal(["changes[1].visible"], Paths(Changed(Hide(new PlanChangeTarget { Id = "dig" }), Hide(new PlanChangeTarget { Role = "marker" }))));
    }

    [Fact]
    public void WithNeededFeatures_ListsTheModule_BetweenTheLabelsAndTheChanges()
    {
        var plan = Full with { Features = ScenePlan.BasicFeatures, ModuleFolders = [] };

        var listed = ScenePlanBuilder.WithNeededFeatures(plan);

        Assert.Equal(ScenePlan.PlayerFeatures, listed.Features);
        Assert.Empty(Paths(listed));
        // Nothing to annotate, nothing to list.
        var none = ScenePlanBuilder.WithNeededFeatures(plan with { Annotations = [], Changes = null });
        Assert.DoesNotContain(ScenePlan.AnnotationsFeature, none.Features);
    }

    [Fact]
    public void TheFilesOfAPlan_IncludeEveryPhotoOnce()
    {
        var twice = Full with { Annotations = [.. Full.Annotations!, Full.Annotations![3] with { Id = "again" }] };

        var files = PlayerWriter.FilesOf(twice);

        Assert.Equal(
            [("photos/hall.jpg", "photo 'photo-hall'"), ("photos/notice.png", "photo 'notice'")],
            files.Where(f => f.Path.StartsWith("photos/", StringComparison.Ordinal)));
    }
}
