// Annotations as a person writes them: on a spec, where they are checked and written, and as a
// file of their own. What they become in a plan is in Player/AnnotationPlannerTests.

using Therion.Blender.Presets;
using Therion.Cave3D;

namespace Therion.Blender.Tests;

public class AnnotationsSpecTests
{
    private static SceneSpec Valid(params AnnotationSpec[] annotations) => BuiltInPresets.OrbitShowcase().Spec with
    {
        Source = new SourceSpec { PlyPath = "/job/model.ply" },
        Output = new OutputSpec { OutputDirectory = "out" },
        Annotations = annotations,
    };

    private static IEnumerable<string> Paths(params AnnotationSpec[] annotations)
        => SceneSpecValidator.Validate(Valid(annotations)).Select(e => e.Path);

    private static readonly AnnotationSpec Label = new() { Kind = AnnotationKind.Label, Station = "main3", Text = "Sala Mare" };
    private static readonly AnnotationSpec Callout = new() { Kind = AnnotationKind.Callout, Station = "main3", Text = "Sifon" };
    private static readonly AnnotationSpec Marker = new() { Kind = AnnotationKind.Marker, At = new CaveVector3(1, 2, 3) };
    private static readonly AnnotationSpec Photo = new() { Kind = AnnotationKind.Photo, Station = "main3", Image = "hall.jpg" };

    // ---- the validator ----

    [Fact]
    public void OneOfEachKind_IsValid()
    {
        Assert.Empty(Paths(Label, Callout, Marker, Photo));
        Assert.Empty(Paths(
            Label with { Id = "name", Role = "place-name", Size = 2, Color = new ColorRgb(1, 1, 0), From = 1, To = 4, Fade = 0 },
            Callout with { Offset = new CaveVector3(2, 0, 5), Within = 30 },
            Photo with { Image = "/trip/Hall.JPEG", Size = 6, Offset = new CaveVector3(0, 0, 4), Framed = true, Facing = PhotoFacing.Fixed, Normal = new CaveVector3(0, -1, 0) }));
    }

    [Fact]
    public void AnAnnotation_IsAboutAStationOrAPoint_OneOfTheTwo()
    {
        Assert.Equal(["annotations[0].station"], Paths(Marker with { At = null }));
        Assert.Equal(["annotations[0].at"], Paths(Marker with { Station = "main3" }));
        Assert.Equal(["annotations[1].at"], Paths(Label, Marker with { At = new CaveVector3(0, double.NaN, 0) }));
    }

    [Fact]
    public void EachKind_NeedsWhatItShows_AndHasNothingOfAnother()
    {
        Assert.Equal(["annotations[0].text"], Paths(Label with { Text = " " }));
        Assert.Equal(["annotations[0].text"], Paths(Callout with { Text = null }));
        Assert.Equal(["annotations[0].text"], Paths(Marker with { Text = "here" }));
        Assert.Equal(["annotations[0].image"], Paths(Photo with { Image = null }));
        Assert.Equal(["annotations[0].image"], Paths(Photo with { Image = "hall.gif" }));
        Assert.Equal(["annotations[0].image"], Paths(Label with { Image = "hall.jpg" }));
        Assert.Equal(["annotations[0].framed"], Paths(Label with { Framed = true }));
        Assert.Equal(["annotations[0].color"], Paths(Photo with { Color = new ColorRgb(1, 0, 0) }));
        Assert.Equal(["annotations[0].color"], Paths(Marker with { Color = new ColorRgb(1, 0, -1) }));
        Assert.Equal(["annotations[0].size"], Paths(Marker with { Size = 0 }));
        Assert.Equal(["annotations[0].offset"], Paths(Label with { Offset = new CaveVector3(0, 0, 2) }));
        Assert.Equal(["annotations[0].offset"], Paths(Callout with { Offset = CaveVector3.Zero }));
        Assert.Equal(["annotations[0].facing"], Paths(Marker with { Facing = PhotoFacing.Fixed }));
        Assert.Equal(["annotations[0].normal"], Paths(Photo with { Facing = PhotoFacing.Fixed }));
        Assert.Equal(["annotations[0].normal"], Paths(Photo with { Normal = new CaveVector3(0, 1, 0) }));
        Assert.Equal(["annotations[0].kind"], Paths(Label with { Kind = (AnnotationKind)12 }));
        Assert.Equal(["annotations[0].role"], Paths(Label with { Role = "Place Name" }));
    }

    [Fact]
    public void ItsTimes_AreInOrder_AndItsDistanceAndItsFade_ArePositive()
    {
        Assert.Equal(["annotations[0].from"], Paths(Label with { From = -1 }));
        Assert.Equal(["annotations[0].to"], Paths(Label with { From = 4, To = 4 }));
        Assert.Equal(["annotations[0].to"], Paths(Label with { To = 0 }));
        Assert.Equal(["annotations[0].within"], Paths(Label with { Within = 0 }));
        Assert.Equal(["annotations[0].fade"], Paths(Label with { Fade = -0.1 }));
    }

    [Fact]
    public void Ids_AreTheirOwn_OrTheirPlaceInTheList_AndNoTwoAreTheSame()
    {
        Assert.Equal(["annotation-1", "mine", "annotation-3"], new[] { Label, Label with { Id = "mine" }, Marker }.Select((a, i) => a.IdAt(i)));
        Assert.Equal(["annotations[1].id"], Paths(Label with { Id = "a" }, Marker with { Id = "a" }));
        Assert.Equal(["annotations[1].id"], Paths(Label with { Id = "annotation-2" }, Marker));
        Assert.Equal(["annotations"], Paths([.. Enumerable.Repeat(Marker, SceneSpecValidator.MaxAnnotations + 1)]));
    }

    // ---- written with a spec ----

    [Fact]
    public void ASpecWithoutAnnotations_IsWrittenAsItAlwaysWas_AndHasTheHashItHad()
    {
        var spec = Valid();

        Assert.DoesNotContain("annotations", SceneSpecSerializer.Write(spec));
        Assert.Empty(SceneSpecSerializer.Read(SceneSpecSerializer.Write(spec)).Annotations);
        Assert.NotEqual(SceneSpecSerializer.ComputeHash(spec), SceneSpecSerializer.ComputeHash(Valid(Marker)));
        // The hash of the preset as it was before there were annotations: not a byte of it changed.
        var preset = BuiltInPresets.OrbitShowcase().Spec;
        Assert.Equal(SceneSpecSerializer.ComputeHash(preset), SceneSpecSerializer.ComputeHash(preset with { Annotations = [] }));
    }

    [Fact]
    public void Annotations_AreWrittenWithASpec_OnlyWhatEachOneSays_AndReadBackTheSame()
    {
        var spec = Valid(
            Label with { From = 1, To = 4 },
            Photo with { Id = "view", Size = 6, Offset = new CaveVector3(0, 0, 4), Framed = true, Facing = PhotoFacing.Fixed, Normal = new CaveVector3(0, -1, 0), Fade = 0 },
            Marker with { Color = new ColorRgb(1, 0.8, 0), Within = 25 });

        string json = SceneSpecSerializer.Write(spec);

        Assert.Contains("\"annotations\": [", json);
        Assert.Contains("\"kind\": \"Label\"", json);
        Assert.Contains("\"at\": [\n        1,\n        2,\n        3\n      ]", json.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("\"framed\": false", json);
        Assert.DoesNotContain("\"facing\": \"Camera\"", json);
        Assert.Contains("\"fade\": 0", json);
        var read = SceneSpecSerializer.Read(json);
        Assert.Equal(spec.Annotations, read.Annotations);
        Assert.Equal(SceneSpecSerializer.ComputeHash(spec), SceneSpecSerializer.ComputeHash(read));
    }

    // ---- a file of annotations ----

    [Fact]
    public void AnAnnotationsFile_IsAList_WithPointsAsThreeNumbers_AndPhotosNamedFromWhereItLies()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"thide-annotations-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "notes.json");
            File.WriteAllText(file, """
                [
                  // where the trip went in
                  { "kind": "callout", "station": "main0", "text": "Intrarea", "from": 0, "to": 4.5 },
                  { "id": "view", "kind": "photo", "station": "main6", "image": "photos/hall.jpg", "size": 6, "within": 40,
                    "framed": true, "facing": "fixed", "normal": [0, -1, 0], "offset": { "x": 0, "y": 0, "z": 4 } },
                  { "kind": "Marker", "at": [512040.5, 5120010.25, 1180], "color": [1, 0.8, 0], "fade": 0, },
                ]
                """);

            var read = AnnotationsFile.ReadFile(file);

            Assert.Equal(3, read.Count);
            Assert.Equal(new AnnotationSpec { Kind = AnnotationKind.Callout, Station = "main0", Text = "Intrarea", From = 0, To = 4.5 }, read[0]);
            Assert.Equal(
                new AnnotationSpec
                {
                    Id = "view", Kind = AnnotationKind.Photo, Station = "main6", Image = Path.Combine(folder, "photos", "hall.jpg"), Size = 6, Within = 40,
                    Framed = true, Facing = PhotoFacing.Fixed, Normal = new CaveVector3(0, -1, 0), Offset = new CaveVector3(0, 0, 4),
                },
                read[1]);
            Assert.Equal(
                new AnnotationSpec { Kind = AnnotationKind.Marker, At = new CaveVector3(512040.5, 5120010.25, 1180), Color = new ColorRgb(1, 0.8, 0), Fade = 0 },
                read[2]);
            Assert.Empty(SceneSpecValidator.Validate(Valid([.. read])));

            // Written again, it reads the same; and the list may stand under a name of its own.
            Assert.Equal(read, AnnotationsFile.Read(AnnotationsFile.Write(read)));
            Assert.Equal(read[0], Assert.Single(AnnotationsFile.Read("""{ "annotations": [{ "kind": "callout", "station": "main0", "text": "Intrarea", "from": 0, "to": 4.5 }] }""")));
            // Without a folder a photo's name is left as it is written.
            Assert.Equal("photos/hall.jpg", AnnotationsFile.Read("""[{ "kind": "photo", "station": "a", "image": "photos/hall.jpg" }]""")[0].Image);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{ "kind": "label" }""", "a list of annotations")]
    [InlineData("""{ "annotations": [], "version": 1 }""", "a list of annotations")]
    [InlineData("\"label\"", "a list of annotations")]
    [InlineData("[null]", "An annotation is missing")]
    [InlineData("""[{ "kind": "label", "staton": "main3" }]""", "staton")]
    [InlineData("""[{ "kind": "arrow", "station": "main3" }]""", "kind")]
    [InlineData("""[{ "kind": "marker", "at": [1, 2] }]""", "Expected three numbers: [x, y, z].")]
    [InlineData("""[{ "kind": "marker", "at": { "x": 1, "y": 2 } }]""", "Expected three numbers: [x, y, z].")]
    [InlineData("""[{ "kind": "marker", "station": "a", "color": "red" }]""", "Expected three numbers: [r, g, b].")]
    [InlineData("[{ \"kind\": \"marker\"", "Malformed annotations JSON")]
    public void WhatIsNoListOfAnnotations_IsRefused_WithWhatIsWrong(string json, string expected)
    {
        var ex = Assert.Throws<SceneSpecFormatException>(() => AnnotationsFile.Read(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void AFileThatIsNotJsonAtAll_IsRefused_WithWhereItStopsBeingJson_AndNothingOfWhatItHolds()
    {
        // Somebody's notes, taken for a list of annotations: sixty thousand lines of them.
        string notes = string.Concat(Enumerable.Range(0, 60_000).Select(i => $"note number {i} about the cave\n"));

        var ex = Assert.Throws<SceneSpecFormatException>(() => AnnotationsFile.Read(notes));

        Assert.StartsWith("Malformed annotations JSON: the text is not JSON at line 1, byte ", ex.Message);
        Assert.True(ex.Message.Length < 90 && !ex.Message.Contains("note"), ex.Message[..Math.Min(ex.Message.Length, 200)]);
        ex = Assert.Throws<SceneSpecFormatException>(() => AnnotationsFile.Read("[\n  { \"kind\": \"marker\", \"station\": hidden words }\n]"));
        Assert.StartsWith("Malformed annotations JSON: the text is not JSON at line 2, byte ", ex.Message);
        Assert.DoesNotContain("hidden", ex.Message);
        // What is wrong with a list that is JSON is said in a line, however long the list.
        string many = "[" + string.Join(",", Enumerable.Repeat("{ \"kind\": \"marker\", \"station\": \"a\" }", 5_000)) + ", 7]";
        ex = Assert.Throws<SceneSpecFormatException>(() => AnnotationsFile.Read(many));
        Assert.InRange(ex.Message.Length, 30, 340);
    }
}
