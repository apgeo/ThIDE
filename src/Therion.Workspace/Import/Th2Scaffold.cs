// TH2-04 — new-scrap scaffolding. Generates a minimal `.th2` stub (encoding + scrap…endscrap),
// optionally wired to an underlying `.xvi` sketch, plus the `input` line to add to the survey's
// `.th`. Pure string output; the caller writes/opens the files.

using System.Text;

namespace Therion.Workspace.Import;

public static class Th2Scaffold
{
    /// <summary>
    /// A <c>.th2</c> stub containing one empty scrap. When <paramref name="sketchXviRelPath"/> is set,
    /// the scrap is wired to that <c>.xvi</c> via <c>-sketch</c>.
    /// </summary>
    public static string NewScrap(string scrapId, string projection = "plan",
        string? sketchXviRelPath = null)
    {
        var id = Sanitize(scrapId);
        var sb = new StringBuilder();
        sb.Append("encoding utf-8\n\n");
        sb.Append("scrap ").Append(id).Append(" -projection ").Append(projection);
        if (!string.IsNullOrWhiteSpace(sketchXviRelPath))
            sb.Append(" -sketch \"").Append(sketchXviRelPath).Append("\" 0 0");
        sb.Append('\n');
        sb.Append("  # draw points / lines / areas here (e.g. with Mapiah)\n");
        sb.Append("endscrap\n");
        return sb.ToString();
    }

    /// <summary>The <c>input</c> line to add to a <c>.th</c> so it pulls in <paramref name="th2RelPath"/>.</summary>
    public static string InputLine(string th2RelPath) => $"input {Quote(th2RelPath)}";

    private static string Quote(string p) => p.Contains(' ') ? $"\"{p}\"" : p;

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.Length == 0 ? "scrap1" : sb.ToString();
    }
}
