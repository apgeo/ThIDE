// Output-collector tests (BA-B11 batch 1): read-back + verification for each output kind,
// including Blender's decorated filenames, partial/empty/missing detection, and the container
// extension mapping. Uses a temp directory with hand-created files.

using Therion.Blender;
using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class OutputCollectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "thide-out-" + Guid.NewGuid().ToString("N"));

    public OutputCollectorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private void Write(string name, int bytes = 16) => File.WriteAllBytes(Path.Combine(_dir, name), new byte[bytes]);

    private OutputSpec Output(OutputKind kind, string baseName = "cave", VideoContainer container = VideoContainer.Mp4)
        => new() { Kind = kind, Container = container, OutputDirectory = _dir, BaseName = baseName };

    // ---- video ----

    [Fact]
    public void Video_ExactName_IsCollected()
    {
        Write("cave.mp4");
        var result = OutputCollector.Collect(Output(OutputKind.Video), frameCount: 120);
        Assert.True(result.Verified);
        Assert.Equal(Path.Combine(_dir, "cave.mp4"), Assert.Single(result.Files).Path);
    }

    [Fact]
    public void Video_FrameRangeDecoratedName_IsStillFound()
    {
        // Blender can decorate a video name with the frame range; prefix match catches it.
        Write("cave0001-0120.mp4");
        var result = OutputCollector.Collect(Output(OutputKind.Video), frameCount: 120);
        Assert.True(result.HasOutputs);
        Assert.Contains("cave0001-0120.mp4", result.Files[0].Path);
    }

    [Fact]
    public void Video_WebmExtension_IsMatched()
    {
        Write("cave.webm");
        var result = OutputCollector.Collect(Output(OutputKind.Video, container: VideoContainer.WebM), frameCount: 10);
        Assert.True(result.HasOutputs);
    }

    [Fact]
    public void Video_Missing_ReportsAProblem()
    {
        var result = OutputCollector.Collect(Output(OutputKind.Video), frameCount: 10);
        Assert.False(result.HasOutputs);
        Assert.NotNull(result.Problem);
    }

    // ---- frame sequence ----

    [Fact]
    public void FrameSequence_AllFrames_AreVerified()
    {
        Write("cave_0001.png"); Write("cave_0002.png"); Write("cave_0003.png");
        var result = OutputCollector.Collect(Output(OutputKind.FrameSequence), frameCount: 3);
        Assert.True(result.Verified);
        Assert.Equal(3, result.Files.Count);
        Assert.Equal(["cave_0001.png", "cave_0002.png", "cave_0003.png"], result.Files.Select(f => Path.GetFileName(f.Path)));
    }

    [Fact]
    public void FrameSequence_Partial_IsReturnedButUnverified()
    {
        Write("cave_0001.png"); Write("cave_0002.png"); // only 2 of 5
        var result = OutputCollector.Collect(Output(OutputKind.FrameSequence), frameCount: 5);
        Assert.True(result.HasOutputs);
        Assert.False(result.Verified);
        Assert.Contains("5", result.Problem);
    }

    // ---- still ----

    [Fact]
    public void Still_IsCollected()
    {
        Write("cave.png");
        var result = OutputCollector.Collect(Output(OutputKind.Still), frameCount: 1);
        Assert.True(result.Verified);
        Assert.Single(result.Files);
    }

    // ---- verification of empties ----

    [Fact]
    public void EmptyFiles_AreExcluded_AndFlagged()
    {
        Write("cave.mp4", bytes: 0); // zero-byte = failed write
        var result = OutputCollector.Collect(Output(OutputKind.Video), frameCount: 10);
        Assert.False(result.HasOutputs);
        Assert.Contains("empty", result.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingDirectory_IsHandled()
    {
        var spec = new OutputSpec { Kind = OutputKind.Video, OutputDirectory = Path.Combine(_dir, "nope"), BaseName = "cave" };
        var result = OutputCollector.Collect(spec, frameCount: 1);
        Assert.False(result.HasOutputs);
        Assert.Contains("directory", result.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(VideoContainer.Mp4, ".mp4")]
    [InlineData(VideoContainer.Mkv, ".mkv")]
    [InlineData(VideoContainer.WebM, ".webm")]
    public void ContainerExtensions_Map(VideoContainer container, string expected)
    {
        Assert.Equal(expected, OutputCollector.ContainerExtension(container));
    }
}
