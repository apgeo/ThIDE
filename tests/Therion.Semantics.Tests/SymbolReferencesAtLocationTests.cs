// Find All References from a token in the editor: a bare station id names the station of the survey it
// is written in. Two files each with a survey holding station `3`: from the second file, `3` must list
// that survey's station, not the first survey's.

using System.Collections.Generic;
using System.Linq;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace Therion.Semantics.Tests;

public class SymbolReferencesAtLocationTests
{
    private const string MainText = """
        survey main
          centreline
            data normal from to length compass clino
            2 3 10 0 0
            3 4 10 90 0
          endcentreline
        endsurvey
        """;

    private const string SideText = """
        survey side
          centreline
            data normal from to length compass clino
            1 3 5 180 0
            3 5 5 270 0
          endcentreline
        endsurvey
        """;

    private static WorkspaceSemanticModel Build(params (string Path, string Text)[] files)
    {
        var parsed = new Dictionary<string, ParseResult<TherionFile>>();
        foreach (var (path, text) in files)
            parsed[path] = new ThParser().Parse(path, text);
        return WorkspaceSemanticModel.Build(parsed, System.Array.Empty<XviFile>(), _ => true);
    }

    private static WorkspaceSemanticModel TwoFiles() => Build(("/p/main.th", MainText), ("/p/side.th", SideText));

    /// <summary>The offset of the <paramref name="nth"/> (0-based) whole token <paramref name="token"/> in <paramref name="text"/>.</summary>
    private static int OffsetOf(string text, string token, int nth)
    {
        int found = -1, at = -1;
        for (int i = 0; i + token.Length <= text.Length; i++)
        {
            bool before = i == 0 || char.IsWhiteSpace(text[i - 1]);
            bool after = i + token.Length == text.Length || char.IsWhiteSpace(text[i + token.Length]);
            if (before && after && string.CompareOrdinal(text, i, token, 0, token.Length) == 0 && ++found == nth) { at = i; break; }
        }
        Assert.True(at >= 0, $"token '{token}' #{nth} not found");
        return at;
    }

    [Fact]
    public void A_bare_station_id_in_the_second_file_lists_that_files_station()
    {
        var ws = TwoFiles();
        int offset = OffsetOf(SideText, "3", 0);

        var refs = SymbolReferences.FindAll(ws, "3", ReferenceKind.Any, "/p/side.th", offset);

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal("/p/side.th", r.Span.FilePath));
        Assert.Single(refs, r => r.IsDeclaration);
    }

    [Fact]
    public void The_same_token_in_the_first_file_lists_the_first_files_station()
    {
        var ws = TwoFiles();
        int offset = OffsetOf(MainText, "3", 1);   // the second `3`, in the `3 4` row

        var refs = SymbolReferences.FindAll(ws, "3", ReferenceKind.Any, "/p/main.th", offset);

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal("/p/main.th", r.Span.FilePath));
    }

    [Fact]
    public void A_caret_just_after_the_token_still_resolves_it()
    {
        var ws = TwoFiles();
        int offset = OffsetOf(SideText, "3", 1) + 1;

        var refs = SymbolReferences.FindAll(ws, "3", ReferenceKind.Any, "/p/side.th", offset);

        Assert.All(refs, r => Assert.Equal("/p/side.th", r.Span.FilePath));
        Assert.Equal(2, refs.Count);
    }

    [Fact]
    public void A_token_the_index_does_not_hold_resolves_in_its_enclosing_survey()
    {
        var ws = TwoFiles();
        // An offset inside side's survey block but on no indexed token (an unsaved edit, say): the
        // station is still the one of the survey the offset lies in.
        int offset = SideText.IndexOf("centreline", System.StringComparison.Ordinal);

        var refs = SymbolReferences.FindAll(ws, "3", ReferenceKind.Station, "/p/side.th", offset);

        Assert.Equal(2, refs.Count);
        Assert.All(refs, r => Assert.Equal("/p/side.th", r.Span.FilePath));
    }

    [Fact]
    public void Without_a_location_the_name_lookup_is_unchanged()
    {
        var ws = TwoFiles();
        var withLocation = SymbolReferences.FindAll(ws, "3", ReferenceKind.Any, null, -1);
        var byName = SymbolReferences.FindAll(ws, "3", ReferenceKind.Any);

        Assert.Equal(byName.Select(r => r.Span), withLocation.Select(r => r.Span));
    }
}
