// The 3-tier progress parser (BA-B10, D-08) — the testable core of the runner. Fed one
// output line at a time, it emits RenderProgress ticks and captures terminal facts
// (device/error/output/done/cancel). Pure: no process, no I/O.
//
//   Tier 1 (primary): our generated script's `THIDE:key=value` lines (phase/frames/frame/
//     device/output/error/done/render-cancel). See the key list atop Emit/ScriptGenerator.cs.
//   Tier 2 (fallback): Blender's own `Fra:N …` lines, used ONLY while tier 1 is silent
//     (a user-edited script) — total comes from the expected frame count seeded here.
//   Tier 3 (spinner): a null-Fraction tick the runner emits at start; not this class.

using System.Globalization;
using System.Text.RegularExpressions;

namespace Therion.Blender.Execution;

/// <summary>Parses render output into <see cref="RenderProgress"/> ticks (D-08 tiers 1–2).</summary>
public sealed partial class RenderProgressParser
{
    private readonly int? _expectedFrameCount;
    private readonly List<string> _warnings = [];
    private int _lastNativeFrame = -1;
    private bool _inTraceback;

    /// <param name="expectedFrameCount">Total frames the render should produce, so the tier-2
    /// native fallback can compute a fraction (tier 1 carries its own total).</param>
    public RenderProgressParser(int? expectedFrameCount = null) => _expectedFrameCount = expectedFrameCount;

    /// <summary>Render device actually used (e.g. "OPTIX", "CPU", "EEVEE"), once reported.</summary>
    public string? Device { get; private set; }

    /// <summary>The script's error message (<c>THIDE:error</c>), if it failed.</summary>
    public string? Error { get; private set; }

    /// <summary>The first uncaught Python exception line (e.g. <c>TypeError: …</c>) captured
    /// from a traceback in the output — failures the script's own <c>fail()</c> never saw.</summary>
    public string? PythonException { get; private set; }

    /// <summary>The final output path the script reported (<c>THIDE:output</c>).</summary>
    public string? OutputPath { get; private set; }

    /// <summary>True once the script printed <c>THIDE:done</c>.</summary>
    public bool Done { get; private set; }

    /// <summary>True if the script signalled a cancelled render.</summary>
    public bool Cancelled { get; private set; }

    /// <summary>True once any tier-1 <c>THIDE:</c> line was seen (disables tier 2).</summary>
    public bool SawStructured { get; private set; }

    public int? FrameCount { get; private set; }

    public int? CurrentFrame { get; private set; }

    /// <summary>Non-fatal notices the script surfaced (label caps, missing HDRI, …).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Consumes one output line; returns a tick when it advances visible progress.</summary>
    public RenderProgress? Consume(string? line)
    {
        if (line is null) return null;
        const string prefix = "THIDE:";
        int idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx >= 0)
        {
            SawStructured = true;
            return ParseStructured(line.AsSpan(idx + prefix.Length).Trim());
        }
        // Python traceback capture: frames are indented; the first non-indented line after
        // "Traceback" is the exception itself (the first traceback = the root cause).
        if (line.StartsWith("Traceback (most recent call last)", StringComparison.Ordinal))
        {
            _inTraceback = true;
            return null;
        }
        if (_inTraceback)
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                // Only exception-shaped lines ("TypeError: …", bare "KeyboardInterrupt")
                // count — a native "Segmentation fault" mid-traceback stays a crash.
                _inTraceback = false;
                if (PythonExceptionRegex().IsMatch(line)) PythonException ??= line.Trim();
            }
            return null;
        }
        return SawStructured ? null : ParseNative(line);
    }

    private RenderProgress? ParseStructured(ReadOnlySpan<char> body)
    {
        int eq = body.IndexOf('=');
        if (eq < 0) return null;
        var key = body[..eq].Trim();
        var value = body[(eq + 1)..].Trim();

        if (key.SequenceEqual("phase"))
            return new RenderProgress(RenderPhase.Rendering, PhaseMessage(value), Device: Device);

        if (key.SequenceEqual("device"))
        {
            Device = value.ToString();
            return new RenderProgress(RenderPhase.Rendering, $"Rendering on {Device}", Device: Device);
        }

        if (key.SequenceEqual("frames"))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total))
                FrameCount = total;
            return null;
        }

        if (key.SequenceEqual("frame"))
            return ParseFrameFraction(value);

        if (key.SequenceEqual("output")) { OutputPath = value.ToString(); return null; }
        if (key.SequenceEqual("error")) { Error = value.ToString(); return null; }
        if (key.SequenceEqual("done")) { Done = true; return null; }
        if (key.SequenceEqual("render-cancel")) { Cancelled = true; return null; }
        if (key.SequenceEqual("warning") || key.SequenceEqual("label-cap")) { _warnings.Add(value.ToString()); return null; }

        return null; // spec-hash / blender / triangles: informational
    }

    private RenderProgress? ParseFrameFraction(ReadOnlySpan<char> value)
    {
        int slash = value.IndexOf('/');
        if (slash < 0) return null;
        if (!int.TryParse(value[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
        if (!int.TryParse(value[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)) return null;

        CurrentFrame = n;
        FrameCount = m;
        double? fraction = m > 0 ? Math.Clamp((double)n / m, 0.0, 1.0) : null;
        return new RenderProgress(RenderPhase.Rendering, $"Rendering frame {n} of {m}", fraction, n, m, Device: Device);
    }

    private RenderProgress? ParseNative(string line)
    {
        var match = NativeFrameRegex().Match(line);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
        if (n == _lastNativeFrame) return null; // Blender reprints Fra: within a frame
        _lastNativeFrame = n;

        CurrentFrame = n;
        int? total = _expectedFrameCount;
        double? fraction = total is > 0 ? Math.Clamp((double)n / total.Value, 0.0, 1.0) : null;
        string message = total is > 0 ? $"Rendering frame {n} of {total}" : $"Rendering frame {n}";
        return new RenderProgress(RenderPhase.Rendering, message, fraction, n, total, Device: Device);
    }

    private static string PhaseMessage(ReadOnlySpan<char> phase) => phase switch
    {
        _ when phase.SequenceEqual("scene") => "Preparing scene",
        _ when phase.SequenceEqual("import") => "Importing model",
        _ when phase.SequenceEqual("engine") => "Configuring engine",
        _ when phase.SequenceEqual("render") => "Rendering",
        _ => "Working",
    };

    [GeneratedRegex(@"^Fra:(\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NativeFrameRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*(:.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PythonExceptionRegex();
}
