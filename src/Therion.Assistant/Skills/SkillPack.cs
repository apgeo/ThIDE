using System.Reflection;
using System.Text;

namespace Therion.Assistant.Skills;

/// <summary>
/// The skill pack: what a model needs to know about Therion and ThIDE, added to the assistant's
/// system prompt. Three layers, in order: the built-in text, the user's own notes, the project's.
/// </summary>
public static class SkillPack
{
    /// <summary>Each layer is cut to this many bytes (UTF-8) so a large file cannot crowd out the conversation.</summary>
    public const int MaxLayerBytes = 8192;

    /// <summary>The built-in text (also printed by <c>therion-mcp skills print</c> for LM Studio's preset).</summary>
    public static string BuiltIn { get; } = ReadBuiltIn();

    private static string ReadBuiltIn()
    {
        using var stream = typeof(SkillPack).Assembly.GetManifestResourceStream("Therion.Assistant.Skills.thide-skills.md");
        return stream is null ? "" : new StreamReader(stream, Encoding.UTF8).ReadToEnd();
    }

    /// <summary>The pack for a session: built-in, then the user's and the project's notes (null = absent).</summary>
    /// <returns>The text, and a warning for each layer that had to be shortened.</returns>
    public static (string Text, IReadOnlyList<string> Warnings) Assemble(string? user, string? project)
    {
        var warnings = new List<string>();
        var sb = new StringBuilder(BuiltIn.TrimEnd());
        void Layer(string title, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var (cut, shortened) = Cut(text.Trim());
            if (shortened) warnings.Add($"{title} is longer than {MaxLayerBytes} bytes and was shortened.");
            sb.Append("\n\n## ").Append(title).Append("\n\n").Append(cut);
        }
        Layer("The user's notes", user);
        Layer("This project's notes", project);
        return (sb.ToString(), warnings);
    }

    private static (string Text, bool Shortened) Cut(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= MaxLayerBytes) return (text, false);
        int chars = text.Length;
        while (chars > 0 && Encoding.UTF8.GetByteCount(text.AsSpan(0, chars)) > MaxLayerBytes) chars = chars * 9 / 10;
        if (chars > 0 && char.IsHighSurrogate(text[chars - 1])) chars--;
        return (text[..chars] + "\n…", true);
    }
}
