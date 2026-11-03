// Every batch editor (the agent's MutationEngine, the app's edit service, an open editor buffer) splices
// through TextSplicer, so its contract is checked here over generated texts and edit sets rather than a
// handful of examples: for edits that do not overlap, the result is the edits applied one at a time from
// the end of the text, whatever order they are given in; same-offset insertions keep the order given;
// and any overlap, out-of-range edit or stale expected slice refuses the whole set.

using System.Text;
using Therion.Workspace.Editing;

namespace Therion.Workspace.Tests.Editing;

public class TextSplicerPropertyTests
{
    private const int Seed = 977;
    private const int Cases = 1500;

    private static readonly string Alphabet = "abc xyz019\t\n\r\n#\"ąłśé€";

    private static string Text(Random rng, int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(Alphabet[rng.Next(Alphabet.Length)]);
        return sb.ToString();
    }

    /// <summary>
    /// Non-overlapping edits over <paramref name="text"/>: replacements of real slices, and insertions
    /// (several may share an offset). No insertion sits where a replacement starts — whether it goes
    /// before or after the replaced text is not defined.
    /// </summary>
    private static List<TextEdit> Edits(Random rng, string text)
    {
        var edits = new List<TextEdit>();
        int pos = 0;
        while (pos <= text.Length && edits.Count < 8)
        {
            int gap = rng.Next(0, 4);
            pos += gap;
            if (pos > text.Length) break;
            if (rng.Next(3) == 0)
            {
                int inserts = rng.Next(1, 3);
                for (int i = 0; i < inserts; i++) edits.Add(TextEdit.Insert(pos, $"<{edits.Count}>"));
                // The next replacement must start after this offset.
                if (pos == text.Length) break;
                pos += 1;
            }
            else
            {
                int len = rng.Next(1, Math.Max(2, Math.Min(5, text.Length - pos + 1)));
                if (pos + len > text.Length) break;
                edits.Add(new TextEdit(pos, len, text.Substring(pos, len), Text(rng, rng.Next(0, 4))));
                pos += len;
            }
        }
        return edits;
    }

    /// <summary>Shuffles, but keeps the given order of insertions that share an offset.</summary>
    private static List<TextEdit> Shuffle(Random rng, List<TextEdit> edits)
    {
        var order = edits.Select((e, i) => (e, key: rng.Next())).ToList();
        var groups = order.GroupBy(x => x.e.Length == 0 ? x.e.Start : -1 - edits.IndexOf(x.e)).ToList();
        var shuffled = groups.OrderBy(g => g.Min(x => x.key)).SelectMany(g => g.Select(x => x.e)).ToList();
        return shuffled;
    }

    /// <summary>The reference: apply one at a time, the furthest first, so earlier offsets stay valid.</summary>
    private static string Sequential(string text, List<TextEdit> edits)
    {
        // Same-offset insertions: apply the last given first, so the first given ends up first.
        var ordered = edits.Select((e, i) => (e, i)).OrderByDescending(x => x.e.Start).ThenByDescending(x => x.i);
        foreach (var (e, _) in ordered)
            text = text[..e.Start] + e.NewText + text[e.End..];
        return text;
    }

    [Fact]
    public void Non_overlapping_edits_equal_applying_them_one_at_a_time_from_the_end()
    {
        var rng = new Random(Seed);
        for (int c = 0; c < Cases; c++)
        {
            var text = Text(rng, rng.Next(0, 40));
            var edits = Edits(rng, text);
            var given = Shuffle(rng, edits);

            var spliced = TextSplicer.Splice(text, given);

            Assert.True(spliced == Sequential(text, edits),
                $"seed {Seed} case {c}: text '{text}', edits {string.Join(", ", given)}");
        }
    }

    [Fact]
    public void Any_overlap_out_of_range_edit_or_stale_slice_refuses_the_whole_set()
    {
        var rng = new Random(Seed + 1);
        int tried = 0;
        for (int c = 0; c < Cases; c++)
        {
            var text = Text(rng, rng.Next(2, 40));
            var edits = Edits(rng, text);
            var replacements = edits.Where(e => e.Length > 0).ToList();
            if (replacements.Count == 0) continue;
            var victim = replacements[rng.Next(replacements.Count)];
            int at = edits.IndexOf(victim);

            TextEdit broken;
            switch (rng.Next(4))
            {
                case 0:   // the slice moved: the file no longer holds what the edit was planned against
                    var stale = victim.ExpectedText[0] == 'Z' ? "Y" + victim.ExpectedText[1..] : "Z" + victim.ExpectedText[1..];
                    broken = victim with { ExpectedText = stale };
                    break;
                case 1:   // runs past the end
                    broken = new TextEdit(text.Length - 1, 2, text[^1..] + "?", "x");
                    break;
                case 2:   // starts before the text
                    broken = new TextEdit(-1, 1, "?", "x");
                    break;
                default:  // overlaps another edit: the victim reaches one character into the next one
                    var next = edits.Where(e => e.Start >= victim.End && e.Length > 0).OrderBy(e => e.Start).FirstOrDefault();
                    if (next is null) continue;
                    int len = next.Start - victim.Start + 1;
                    broken = new TextEdit(victim.Start, len, text.Substring(victim.Start, len), "x");
                    break;
            }
            tried++;
            var mutated = edits.ToList();
            mutated[at] = broken;

            Assert.True(TextSplicer.Splice(text, Shuffle(rng, mutated)) is null,
                $"seed {Seed + 1} case {c}: text '{text}', accepted the broken edit {broken}");
        }
        Assert.True(tried > Cases / 2, $"only {tried} broken sets were generated");
    }

    [Fact]
    public void A_set_that_splices_also_splices_from_a_copy_of_the_result_in_reverse()
    {
        // Inverting every edit against the result and splicing those gives the original text back:
        // nothing outside the edits moved, and each edit landed exactly where it was planned.
        var rng = new Random(Seed + 2);
        for (int c = 0; c < Cases; c++)
        {
            var text = Text(rng, rng.Next(0, 40));
            var edits = Edits(rng, text).Where(e => e.Length > 0 || e.NewText.Length > 0).ToList();
            // Same-offset insertions collapse into one region of the result; keep one per offset.
            edits = edits.GroupBy(e => (e.Start, e.Length == 0)).Select(g => g.First()).ToList();
            var result = TextSplicer.Splice(text, Shuffle(rng, edits));
            Assert.NotNull(result);

            var inverse = new List<TextEdit>();
            int shift = 0;
            foreach (var e in edits.OrderBy(e => e.Start))
            {
                inverse.Add(new TextEdit(e.Start + shift, e.NewText.Length, e.NewText, e.ExpectedText));
                shift += e.NewText.Length - e.Length;
            }
            // Adjacent empty-to-empty pieces would make two zero-length inverse edits at one offset; skip those.
            if (inverse.GroupBy(e => e.Start).Any(g => g.Count() > 1)) continue;

            Assert.True(TextSplicer.Splice(result!, inverse) == text,
                $"seed {Seed + 2} case {c}: text '{text}', edits {string.Join(", ", edits)}");
        }
    }
}
