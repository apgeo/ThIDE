// Adding members (scraps, sub-maps) to a `map … endmap` block as one text insertion: after the last
// member's line (or under the header of an empty map), with its indentation and the file's line
// endings, skipping members already there. Shared by the MCP add_map_members tool and the IDE's
// "Add unreferenced scraps to maps".

using System.Collections.Immutable;
using Therion.Syntax;

namespace Therion.Workspace.Editing;

public enum MapEditProblem { None, MapNotFound, Unterminated }

/// <param name="Edit">The insertion, or null when there is nothing to add (or a problem).</param>
/// <param name="Added">The members the edit adds, in order.</param>
public sealed record MapMemberPlan(TextEdit? Edit, IReadOnlyList<string> Added, MapEditProblem Problem);

public static class MapMemberEdits
{
    public static MapMemberPlan Plan(string filePath, string text, string mapId, IEnumerable<string> members)
    {
        var parsed = TherionWorkspace.ParseText(filePath, text);
        if (parsed.Value is null || FindMap(parsed.Value.Children, mapId) is not { } block)
            return new MapMemberPlan(null, [], MapEditProblem.MapNotFound);
        // "Before endmap" only means something when there is an endmap.
        if (!block.IsTerminated) return new MapMemberPlan(null, [], MapEditProblem.Unterminated);

        var already = block.Members.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var toAdd = members.Where(m => !already.Contains(m)).Distinct(StringComparer.Ordinal).ToList();
        if (toAdd.Count == 0) return new MapMemberPlan(null, [], MapEditProblem.None);

        var (offset, indent) = InsertionPoint(text, block);
        var newline = NewlineOf(text);
        return new MapMemberPlan(TextEdit.Insert(offset, string.Concat(toAdd.Select(m => newline + indent + m))), toAdd, MapEditProblem.None);
    }

    public static MapCommand? FindMap(ImmutableArray<TherionNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node is MapCommand m && string.Equals(m.Id, id, StringComparison.Ordinal)) return m;
            // Maps nest inside survey blocks, so the search cannot stop at the top level.
            if (node is BlockCommand block && FindMap(block.Children, id) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// Where a new member line goes, and the indentation to give it: at the end of the last member's
    /// line, or of the map header when the body is empty. Anchoring to a line the parser identified
    /// avoids searching the text for `endmap`, which could match one inside a comment.
    /// </summary>
    private static (int Offset, string Indent) InsertionPoint(string text, MapCommand block)
    {
        if (block.Members.Length > 0)
        {
            var last = block.Members[^1].Span.StartOffset;
            return (EndOfLine(text, last), IndentOf(text, last));
        }
        var header = block.Span.StartOffset;
        return (EndOfLine(text, header), IndentOf(text, header) + "  ");
    }

    // The file's own line break: CRLF, else a lone CR (classic Mac files, which the parser reads), else LF.
    private static string NewlineOf(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : text.Contains('\r') && !text.Contains('\n') ? "\r" : "\n";

    // Before the line break: a CR or an LF, so an insertion cannot land between the two of a CRLF.
    private static int EndOfLine(string text, int offset)
    {
        int at = text.IndexOfAny(LineBreaks, Math.Clamp(offset, 0, Math.Max(text.Length - 1, 0)));
        return at < 0 ? text.Length : at;
    }

    private static readonly char[] LineBreaks = ['\r', '\n'];

    private static string IndentOf(string text, int offset)
    {
        if (text.Length == 0) return string.Empty;
        int probe = Math.Clamp(offset, 0, text.Length - 1);
        int start = text.LastIndexOfAny(LineBreaks, probe) + 1;
        int i = start;
        while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
        return text[start..i];
    }
}
