// The captions of a scene as subtitle files, for a player that shows them over the movie or beside
// it: SubRip (.srt), which every video player reads, and WebVTT (.vtt), which a web page's <video>
// element reads. The texts are the ones the movie itself shows; the files let a viewer turn them
// off, search them and translate them. One caption is shown at a time, in the files as in the
// movie. Pure text: nothing here touches a file.

using System.Globalization;
using System.Text;

namespace Therion.Blender;

/// <summary>Writes captions as SubRip and WebVTT subtitles.</summary>
public static class Subtitles
{
    public const string SrtExtension = ".srt";
    public const string VttExtension = ".vtt";

    /// <summary>The captions in SubRip form: numbered cues in order of their start, times as
    /// <c>00:00:02,500</c>. Empty when there are none. Lines end in LF.</summary>
    public static string Srt(IEnumerable<CaptionSpec> captions)
    {
        var text = new StringBuilder();
        int number = 0;
        foreach (var caption in Ordered(captions))
        {
            text.Append(++number).Append('\n');
            text.Append(Time(caption.Start, ',')).Append(" --> ").Append(Time(caption.End, ',')).Append('\n');
            text.Append(Lines(caption.Text)).Append("\n\n");
        }
        return text.ToString();
    }

    /// <summary>The captions in WebVTT form: the <c>WEBVTT</c> header, then cues in order of
    /// their start, times as <c>00:00:02.500</c>. With no captions, the header alone.</summary>
    public static string Vtt(IEnumerable<CaptionSpec> captions)
    {
        var text = new StringBuilder("WEBVTT\n");
        foreach (var caption in Ordered(captions))
        {
            text.Append('\n');
            text.Append(Time(caption.Start, '.')).Append(" --> ").Append(Time(caption.End, '.')).Append('\n');
            // A cue's text is markup: these three characters would be read as part of it.
            text.Append(Lines(caption.Text).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// The captions as they are shown: in order of their start, one at a time. Where two overlap
    /// the later one replaces the earlier, which ends when the later starts; of two that start
    /// together, the one listed later is shown. Captions without text or without time are left
    /// out.
    /// </summary>
    public static IReadOnlyList<CaptionSpec> OneAtATime(IEnumerable<CaptionSpec> captions)
    {
        ArgumentNullException.ThrowIfNull(captions);
        var ordered = captions
            .Where(c => c is not null && !string.IsNullOrWhiteSpace(c.Text) && c.End > c.Start)
            .Select((caption, index) => (caption, index))
            .OrderBy(c => c.caption.Start).ThenBy(c => c.index)
            .Select(c => c.caption)
            .ToList();
        var shown = new List<CaptionSpec>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var caption = ordered[i];
            if (i + 1 < ordered.Count && ordered[i + 1].Start < caption.End)
            {
                if (ordered[i + 1].Start <= caption.Start) continue; // replaced before it is seen
                caption = caption with { End = ordered[i + 1].Start };
            }
            shown.Add(caption);
        }
        return shown;
    }

    private static IReadOnlyList<CaptionSpec> Ordered(IEnumerable<CaptionSpec> captions) => OneAtATime(captions);

    /// <summary>
    /// Reads a caption written on one line as <c>&lt;start&gt;-&lt;end&gt;:&lt;text&gt;</c>, the times in
    /// seconds with a dot for the decimals: <c>2.5-6:Sala Mare</c>. For a command line, where a
    /// caption is one argument.
    /// </summary>
    public static bool TryParseCaption(string? line, out CaptionSpec caption)
    {
        caption = new CaptionSpec();
        if (line is null) return false;
        int colon = line.IndexOf(':');
        int dash = colon < 0 ? -1 : line.IndexOf('-', 0, colon);
        if (dash <= 0) return false;
        const NumberStyles style = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (!double.TryParse(line.AsSpan(0, dash), style, CultureInfo.InvariantCulture, out double start)
            || !double.TryParse(line.AsSpan(dash + 1, colon - dash - 1), style, CultureInfo.InvariantCulture, out double end))
            return false;
        string text = line[(colon + 1)..].Trim();
        if (text.Length == 0 || !(start >= 0) || !(end > start)) return false;
        caption = new CaptionSpec { Text = text, Start = start, End = end };
        return true;
    }

    /// <summary>A cue's text: its lines without the empty ones, which would end the cue.</summary>
    private static string Lines(string text)
        => string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));

    private static string Time(double seconds, char decimalMark)
    {
        long milliseconds = (long)Math.Round(Math.Max(0.0, seconds) * 1000.0, MidpointRounding.AwayFromZero);
        long hours = milliseconds / 3_600_000, minutes = milliseconds / 60_000 % 60, secs = milliseconds / 1000 % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{secs:00}{decimalMark}{milliseconds % 1000:000}");
    }
}
