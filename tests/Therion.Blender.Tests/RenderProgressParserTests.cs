// Progress-parser tests (BA-B10 batch 2): the three-tier protocol — THIDE structured lines,
// the native Fra: fallback (only while tier 1 is silent), and capture of device/error/
// output/done/cancel/warnings — plus malformed-line tolerance.

using Therion.Blender;
using Therion.Blender.Execution;

namespace Therion.Blender.Tests;

public class RenderProgressParserTests
{
    private static List<RenderProgress> Feed(RenderProgressParser parser, params string[] lines)
    {
        var ticks = new List<RenderProgress>();
        foreach (var line in lines)
            if (parser.Consume(line) is { } tick) ticks.Add(tick);
        return ticks;
    }

    // ---- tier 1 (THIDE structured) ----

    [Fact]
    public void Structured_FrameLine_YieldsFraction()
    {
        var parser = new RenderProgressParser();
        var tick = parser.Consume("THIDE:frame=60/240");
        Assert.NotNull(tick);
        Assert.Equal(RenderPhase.Rendering, tick!.Value.Phase);
        Assert.Equal(60, tick.Value.Frame);
        Assert.Equal(240, tick.Value.FrameCount);
        Assert.Equal(0.25, tick.Value.Fraction!.Value, 6);
    }

    [Fact]
    public void Structured_CapturesDevicePhasesAndTerminalFacts()
    {
        var parser = new RenderProgressParser();
        Feed(parser,
            "THIDE:spec-hash=abc",           // informational — no tick
            "THIDE:blender=4.5.1",
            "THIDE:phase=scene",
            "THIDE:phase=import",
            "THIDE:device=OPTIX",
            "THIDE:frame=1/2",
            "THIDE:frame=2/2",
            "THIDE:output=/out/cave.mp4",
            "THIDE:done=1");

        Assert.True(parser.SawStructured);
        Assert.True(parser.Done);
        Assert.Equal("OPTIX", parser.Device);
        Assert.Equal("/out/cave.mp4", parser.OutputPath);
        Assert.Null(parser.Error);
    }

    [Fact]
    public void Structured_DeviceRidesLaterFrameTicks()
    {
        var parser = new RenderProgressParser();
        parser.Consume("THIDE:device=CUDA");
        var tick = parser.Consume("THIDE:frame=5/10");
        Assert.Equal("CUDA", tick!.Value.Device);
    }

    [Fact]
    public void Structured_ErrorAndCancelAndWarnings_AreCaptured()
    {
        var parser = new RenderProgressParser();
        parser.Consume("THIDE:label-cap=showing 200 of 3000 station labels");
        parser.Consume("THIDE:warning=HDRI file not found: /x.exr");
        parser.Consume("THIDE:render-cancel=1");
        parser.Consume("THIDE:error=PLY import produced no object");

        Assert.Equal("PLY import produced no object", parser.Error);
        Assert.True(parser.Cancelled);
        Assert.Equal(2, parser.Warnings.Count);
    }

    [Fact]
    public void Structured_PrefixMayBePrecededByBlenderNoise()
    {
        // Blender sometimes prefixes stdout; the parser finds THIDE: anywhere in the line.
        var parser = new RenderProgressParser();
        var tick = parser.Consume("noise THIDE:frame=3/3");
        Assert.Equal(3, tick!.Value.Frame);
    }

    // ---- tier 2 (native fallback) ----

    [Fact]
    public void Native_Fra_UsedOnlyWhenStructuredSilent()
    {
        var parser = new RenderProgressParser(expectedFrameCount: 100);
        var tick = parser.Consume("Fra:25 Mem:120.00M | Time:00:03.21 | Rendering");
        Assert.NotNull(tick);
        Assert.Equal(25, tick!.Value.Frame);
        Assert.Equal(100, tick.Value.FrameCount);
        Assert.Equal(0.25, tick.Value.Fraction!.Value, 6);
    }

    [Fact]
    public void Native_Suppressed_AfterAnyStructuredLine()
    {
        var parser = new RenderProgressParser(expectedFrameCount: 100);
        parser.Consume("THIDE:phase=render"); // tier 1 now active
        Assert.Null(parser.Consume("Fra:50 Mem:...")); // native ignored
    }

    [Fact]
    public void Native_RepeatedSameFrame_IsDeduped()
    {
        var parser = new RenderProgressParser(expectedFrameCount: 10);
        Assert.NotNull(parser.Consume("Fra:3 Mem:1M | Sample 1/64"));
        Assert.Null(parser.Consume("Fra:3 Mem:1M | Sample 32/64")); // same frame, no new tick
        Assert.NotNull(parser.Consume("Fra:4 Mem:1M | Sample 1/64"));
    }

    [Fact]
    public void Native_WithoutExpectedCount_HasNoFraction()
    {
        var parser = new RenderProgressParser(); // no expected count
        var tick = parser.Consume("Fra:7 Rendering");
        Assert.Equal(7, tick!.Value.Frame);
        Assert.Null(tick.Value.Fraction);
    }

    // ---- traceback capture ----

    [Fact]
    public void PythonTraceback_CapturesTheExceptionLine()
    {
        var parser = new RenderProgressParser();
        Feed(parser,
            "Traceback (most recent call last):",
            "  File \"render.py\", line 107, in <module>",
            "    _sky.sky_type = 'NISHITA'",
            "TypeError: bpy_struct: item.attr = val: enum \"NISHITA\" not found",
            "Error: script failed, file: 'render.py', exiting.");
        Assert.Equal("TypeError: bpy_struct: item.attr = val: enum \"NISHITA\" not found", parser.PythonException);
    }

    [Fact]
    public void PythonTraceback_KeepsTheFirstException_AcrossChainedTracebacks()
    {
        var parser = new RenderProgressParser();
        Feed(parser,
            "Traceback (most recent call last):", "  File a", "ValueError: root cause",
            "During handling of the above exception, another exception occurred:",
            "Traceback (most recent call last):", "  File b", "RuntimeError: secondary");
        Assert.Equal("ValueError: root cause", parser.PythonException);
    }

    [Fact]
    public void NativeFaultMidTraceback_IsNotAnException()
    {
        var parser = new RenderProgressParser();
        Feed(parser, "Traceback (most recent call last):", "  File ...", "Segmentation fault");
        Assert.Null(parser.PythonException); // stays a crash, not a ScriptError
    }

    // ---- robustness ----

    [Theory]
    [InlineData("THIDE:frame=bad")]
    [InlineData("THIDE:frame=5")]      // no slash
    [InlineData("THIDE:noequalshere")]
    [InlineData("THIDE:=empty-key")]
    [InlineData("")]
    [InlineData("random blender chatter")]
    public void MalformedLines_ProduceNoTick_AndDoNotThrow(string line)
    {
        var parser = new RenderProgressParser(100);
        Assert.Null(parser.Consume(line));
    }
}
