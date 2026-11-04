// The editor takes a view-model text change as one replace of the part that differs. Over seeded pairs
// of related texts: the replace turns the old text into the new one exactly, and it is the smallest
// such replace that keeps a common prefix and suffix (so the caret, undo and folds outside it stay put).
// THIDE_PROPERTY_RUNS raises the number of cases.

using System.Text;
using ThIDE.Editor;

namespace ThIDE.Tests;

public class BufferSyncProperties
{
    private const int BaseSeed = 0x0B5F_0001;

    private static int Runs(int defaultRuns) =>
        int.TryParse(Environment.GetEnvironmentVariable("THIDE_PROPERTY_RUNS"), out var n) && n > 0 ? n : defaultRuns;

    // Line endings, a surrogate pair and repeated characters make prefix and suffix overlap candidates.
    private static readonly string[] Pieces = ["survey a", "\n", "\r\n", "  ", "1 2 5 0 0", "#", "ș", "😀", "a", "aa", "end", ""];

    private static string Text(Random rnd)
    {
        var sb = new StringBuilder();
        int n = rnd.Next(0, 12);
        for (int i = 0; i < n; i++) sb.Append(Pieces[rnd.Next(Pieces.Length)]);
        return sb.ToString();
    }

    /// <summary>A second text: the first with a few pieces inserted, removed or replaced.</summary>
    private static string Edited(string text, Random rnd)
    {
        var sb = new StringBuilder(text);
        int edits = rnd.Next(1, 4);
        for (int i = 0; i < edits; i++)
        {
            int at = rnd.Next(sb.Length + 1);
            int remove = rnd.Next(Math.Min(4, sb.Length - at) + 1);
            sb.Remove(at, remove).Insert(at, Pieces[rnd.Next(Pieces.Length)]);
        }
        return sb.ToString();
    }

    [Fact]
    public void The_replace_turns_the_old_text_into_the_new_one_and_spans_only_what_differs()
    {
        int runs = Runs(3000);
        for (int i = 0; i < runs; i++)
        {
            int seed = BaseSeed + i;
            var rnd = new Random(seed);
            var current = Text(rnd);
            var text = rnd.Next(4) == 0 ? Text(rnd) : Edited(current, rnd);
            var r = BufferSync.ChangedPart(current, text);
            string at = $"seed {seed}: \"{Escape(current)}\" -> \"{Escape(text)}\" gave ({r.Offset}, {r.Length}, \"{Escape(r.Text)}\")";

            Assert.True(r.Offset >= 0 && r.Length >= 0 && r.Offset + r.Length <= current.Length, at);
            Assert.True(current.Remove(r.Offset, r.Length).Insert(r.Offset, r.Text) == text, at);
            // Nothing kept could have been kept longer: the prefix is the longest common one, and the
            // suffix the longest common one that does not overlap it.
            int max = Math.Min(current.Length, text.Length);
            int prefix = r.Offset, suffix = current.Length - r.Offset - r.Length;
            Assert.True(prefix + suffix <= max, at);
            Assert.True(prefix == max || current[prefix] != text[prefix], at);
            Assert.True(suffix == max - prefix || current[current.Length - 1 - suffix] != text[text.Length - 1 - suffix], at);
            if (current == text) Assert.True(r.Length == 0 && r.Text.Length == 0, at);
        }
    }

    [Theory]
    [InlineData("survey a\nendsurvey\n", "survey a\n  # x\nendsurvey\n", 9, 0, "  # x\n")]
    [InlineData("a\r\nb", "a\nb", 1, 1, "")]                 // a CRLF turned LF: only the CR goes
    [InlineData("abc", "", 0, 3, "")]
    [InlineData("", "abc", 0, 0, "abc")]
    [InlineData("aaa", "aa", 2, 1, "")]                      // a run: prefix and suffix never overlap
    public void Typical_changes_replace_the_expected_span(string current, string text, int offset, int length, string insert) =>
        Assert.Equal(new BufferReplacement(offset, length, insert), BufferSync.ChangedPart(current, text));

    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
}
