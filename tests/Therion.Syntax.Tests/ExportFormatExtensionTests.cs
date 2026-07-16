// CommandVocabulary.ExportFormatForExtension — reaching the `-fmt` keyword from the file a caver
// actually asks for. The lox/loch pair is the one that has already bitten this repo twice (a
// committed eval fixture and the question-matrix doc both wrote `-fmt lox`), so it is pinned here.

using Therion.Syntax;

namespace Therion.Syntax.Tests;

public class ExportFormatExtensionTests
{
    [Theory]
    // The three extensions that are not their own keyword — the same set ModelFormats once carried
    // as phantom formats, which is how we know the trap is real and not theoretical.
    [InlineData(".lox", "loch")]
    [InlineData("lox", "loch")]     // with or without the dot
    [InlineData(".LOX", "loch")]    // extensions are not case-sensitive to a user
    [InlineData(".plt", "compass")]
    [InlineData(".wrl", "vrml")]
    public void An_extension_that_is_not_its_keyword_resolves_to_the_real_format(string ext, string expected)
    {
        Assert.Equal(expected, CommandVocabulary.ExportFormatForExtension("model", ext));
    }

    [Theory]
    // Everything else writes the extension it is named after, so no table is needed — and `3d` is a
    // model format in its own right, which is why `.3d` does not need a special case.
    [InlineData(".kml", "kml")]
    [InlineData(".3d", "3d")]
    [InlineData(".dxf", "dxf")]
    public void An_extension_that_is_its_own_keyword_resolves_to_itself(string ext, string expected)
    {
        Assert.Equal(expected, CommandVocabulary.ExportFormatForExtension("model", ext));
    }

    [Fact]
    public void A_format_valid_for_another_type_does_not_leak_across()
    {
        // .lox is a model, never a map: answering `loch` for `export map` would be a confident lie.
        Assert.Null(CommandVocabulary.ExportFormatForExtension("map", ".lox"));
        Assert.Equal("pdf", CommandVocabulary.ExportFormatForExtension("map", ".pdf"));
        Assert.Equal("svg", CommandVocabulary.ExportFormatForExtension("map", ".svg"));

        // atlas takes pdf only.
        Assert.Equal("pdf", CommandVocabulary.ExportFormatForExtension("atlas", ".pdf"));
        Assert.Null(CommandVocabulary.ExportFormatForExtension("atlas", ".svg"));
    }

    [Fact]
    public void An_unknown_extension_or_type_resolves_to_nothing()
    {
        Assert.Null(CommandVocabulary.ExportFormatForExtension("model", ".zzz"));
        Assert.Null(CommandVocabulary.ExportFormatForExtension("model", ""));
        Assert.Null(CommandVocabulary.ExportFormatForExtension("model", "."));
        Assert.Null(CommandVocabulary.ExportFormatForExtension("not-a-type", ".lox"));
    }

    [Fact]
    public void Every_resolved_format_is_one_the_validator_accepts()
    {
        // The mapping must never name a format IsExportFormat would then reject — help and validation
        // disagreeing is the exact failure this whole line of work exists to prevent.
        foreach (var ext in new[] { ".lox", ".plt", ".wrl", ".kml", ".3d", ".dxf" })
        {
            var fmt = CommandVocabulary.ExportFormatForExtension("model", ext);
            Assert.NotNull(fmt);
            Assert.True(CommandVocabulary.IsExportFormat("model", fmt!), $"{ext} -> {fmt} is rejected");
        }
    }

    [Theory]
    // The other direction: what file does this format write? Not the inverse of the above, because
    // `3d` and `survex` both write .3d — so .3d resolves to `3d` by name, while `survex` still has
    // to be told what it writes.
    [InlineData("loch", ".lox")]
    [InlineData("survex", ".3d")]
    [InlineData("compass", ".plt")]
    [InlineData("vrml", ".wrl")]
    [InlineData("kml", ".kml")]     // named after its extension
    [InlineData("3d", ".3d")]
    public void A_format_reports_the_extension_it_writes(string fmt, string expected)
    {
        Assert.Equal(expected, CommandVocabulary.ExtensionForExportFormat(fmt));
    }

    [Fact]
    public void The_two_directions_agree_wherever_a_format_is_asked_to_round_trip()
    {
        // Every model format's extension must resolve back to a format the compiler accepts for a
        // model. It need not resolve to the SAME name (.3d resolves to `3d`, not `survex`) — but it
        // must never resolve to nothing, or a caller could not get that file at all.
        foreach (var fmt in CommandVocabulary.ExportFormats("model"))
        {
            var ext = CommandVocabulary.ExtensionForExportFormat(fmt);
            var back = CommandVocabulary.ExportFormatForExtension("model", ext);
            Assert.True(back is not null, $"{fmt} writes {ext}, which resolves to no format");
            Assert.True(CommandVocabulary.IsExportFormat("model", back!), $"{ext} -> {back} is rejected");
        }
    }

    [Fact]
    public void The_trap_list_is_scoped_to_the_type_that_has_the_trap()
    {
        // A trap is an extension whose obvious guess is NOT a valid format for that type.
        var model = CommandVocabulary.ForeignExportExtensions("model").ToList();
        Assert.Contains(model, p => p.Key == "lox" && p.Value == "loch");
        Assert.Contains(model, p => p.Key == "plt" && p.Value == "compass");
        Assert.Contains(model, p => p.Key == "wrl" && p.Value == "vrml");
        // .3d is not a model trap: `3d` is itself a model format, so the guess works.
        Assert.DoesNotContain(model, p => p.Key == "3d");
        Assert.Equal(3, model.Count);

        // ...but it IS a map trap. `survex` is a map format while `3d` is not, so guessing `3d`
        // from cave.3d fails for a map and succeeds for a model — which is exactly why the list is
        // computed per type instead of being one global table of "confusing extensions".
        var map = CommandVocabulary.ForeignExportExtensions("map").ToList();
        var mapTrap = Assert.Single(map);
        Assert.Equal("3d", mapTrap.Key);
        Assert.Equal("survex", mapTrap.Value);
        Assert.DoesNotContain(map, p => p.Key == "lox");   // .lox is a model, never a map

        // Nothing to warn about here, so help says nothing rather than padding.
        Assert.Empty(CommandVocabulary.ForeignExportExtensions("database"));
    }

    [Fact]
    public void Lox_is_still_not_a_format_itself()
    {
        // The regression that started all of this: `-fmt lox` must stay invalid.
        Assert.False(CommandVocabulary.IsExportFormat("model", "lox"));
        Assert.True(CommandVocabulary.IsExportFormat("model", "loch"));
    }
}
