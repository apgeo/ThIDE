// The reveal of a SceneSpec: how it is written, read and checked.

namespace Therion.Blender.Tests;

public class RevealSpecTests
{
    private static readonly SceneSpec Plain = new()
    {
        Source = new SourceSpec { PlyPath = "job/model.ply" },
        Output = new OutputSpec { OutputDirectory = "out" },
    };

    private static IEnumerable<string> Paths(SceneSpec spec) => SceneSpecValidator.Validate(spec).Select(e => e.Path);

    [Fact]
    public void ASpecWithoutAReveal_IsWrittenAsItAlwaysWas_AndHasTheHashItHad()
    {
        string json = SceneSpecSerializer.Write(Plain);

        Assert.DoesNotContain("reveal", json);
        Assert.Null(SceneSpecSerializer.Read(json).Reveal);
        // The hash of the default spec, as the releases before the reveal computed it.
        Assert.Equal(SceneSpecSerializer.ComputeHash(Plain), SceneSpecSerializer.ComputeHash(SceneSpecSerializer.Read(json)));
        Assert.NotEqual(SceneSpecSerializer.ComputeHash(Plain), SceneSpecSerializer.ComputeHash(Plain with { Reveal = new RevealSpec() }));
    }

    [Fact]
    public void AReveal_IsWrittenWithWhatItHas_AndReadsBackTheSame()
    {
        var byDate = Plain with { Reveal = new RevealSpec { By = RevealBy.Date, DatesPath = "out/cave.3d", Start = 1, End = 9, Edge = 0.1, Glow = false, Counter = false } };
        string json = SceneSpecSerializer.Write(byDate).ReplaceLineEndings("\n");

        Assert.Contains(
            "  \"reveal\": {\n    \"by\": \"Date\",\n    \"datesPath\": \"out/cave.3d\",\n    \"start\": 1,\n    \"end\": 9,\n    \"edge\": 0.1,\n    \"glow\": false,\n", json);
        Assert.Equal(byDate.Reveal, SceneSpecSerializer.Read(json).Reveal);

        // What is not said is the default: by distance, from the entrances, to the end of the clip.
        string least = SceneSpecSerializer.Write(Plain with { Reveal = new RevealSpec() }).ReplaceLineEndings("\n");
        Assert.DoesNotContain("\"from\"", least);
        Assert.DoesNotContain("\"datesPath\"", least);
        Assert.DoesNotContain("\"end\"", least);
        var read = SceneSpecSerializer.Read(least.Replace("\"by\": \"Distance\"", "\"by\": \"distance\"")).Reveal!;
        Assert.Equal(new RevealSpec(), read);
        Assert.Equal((RevealBy.Distance, 0.0, null, 0.03, true, true), (read.By, read.Start, read.End, read.Edge, read.Glow, read.Counter));
        Assert.Equal(new RevealSpec { From = "main0" }, SceneSpecSerializer.Read(least.Replace("\"by\": \"Distance\",", "\"by\": \"Distance\", \"from\": \"main0\",")).Reveal);
    }

    [Fact]
    public void AReveal_StartsInTheClip_EndsAfterItStarts_AndHasOnlyWhatGoesWithItsKind()
    {
        SceneSpec With(RevealSpec reveal) => Plain with { Reveal = reveal };

        Assert.Empty(Paths(With(new RevealSpec())));
        Assert.Empty(Paths(With(new RevealSpec { By = RevealBy.Distance, From = "main0", Start = 2, End = 30, Edge = 0.5 })));
        Assert.Empty(Paths(With(new RevealSpec { By = RevealBy.Date, DatesPath = "cave.3d", Edge = 0 })));

        Assert.Equal(["reveal.by"], Paths(With(new RevealSpec { By = (RevealBy)7 })));
        Assert.Equal(["reveal.from"], Paths(With(new RevealSpec { By = RevealBy.Order, From = "main0" })));
        Assert.Equal(["reveal.from"], Paths(With(new RevealSpec { From = " " })));
        Assert.Equal(["reveal.datesPath"], Paths(With(new RevealSpec { By = RevealBy.Distance, DatesPath = "cave.3d" })));
        Assert.Equal(["reveal.datesPath"], Paths(With(new RevealSpec { By = RevealBy.Date, DatesPath = "" })));
        Assert.Equal(["reveal.start"], Paths(With(new RevealSpec { Start = -1 })));
        Assert.Equal(["reveal.start"], Paths(With(new RevealSpec { Start = 10 }))); // the clip is ten seconds long
        Assert.Equal(["reveal.end"], Paths(With(new RevealSpec { Start = 4, End = 4 })));
        Assert.Equal(["reveal.end"], Paths(With(new RevealSpec { End = double.PositiveInfinity })));
        Assert.Equal(["reveal.edge"], Paths(With(new RevealSpec { Edge = 0.6 })));
        Assert.Equal(["reveal.edge"], Paths(With(new RevealSpec { Edge = double.NaN })));
        Assert.Equal(["reveal.glowColor"], Paths(With(new RevealSpec { GlowColor = new ColorRgb(1, 2, 0) })));
    }
}
