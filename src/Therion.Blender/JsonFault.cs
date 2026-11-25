// What is passed on of a JSON reader's complaint. The reader's own words quote what it could not
// read, however long that is and whatever file it was that somebody took for a spec, a plan, a
// preset or a list of annotations: a message, a log and an agent's context are no place for the
// contents of a file that was named by mistake.

using System.Text.Json;

namespace Therion.Blender;

internal static class JsonFault
{
    /// <summary>The most of a reader's own message that is passed on.</summary>
    private const int MaxMessage = 300;

    /// <summary>For a text that is not JSON: where it stops being JSON, and nothing of the text.</summary>
    public static string NotJson(JsonException ex)
        => $"the text is not JSON at line {(ex.LineNumber ?? 0) + 1}, byte {(ex.BytePositionInLine ?? 0) + 1}.";

    /// <summary>For JSON of the wrong shape: the reader's first line, which names a key or a
    /// kind of value, cut short.</summary>
    public static string OfShape(JsonException ex)
    {
        string message = ex.Message.Split('\n')[0];
        return message.Length > MaxMessage ? message[..MaxMessage] + "…" : message;
    }
}
