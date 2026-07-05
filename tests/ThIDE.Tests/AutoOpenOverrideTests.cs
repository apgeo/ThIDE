// #7 — per-file auto-open overrides: explicit "always" opens unconditionally, "never" suppresses,
// and unset files follow the general per-type flags (subject to the open-all-vs-first rule).

using System.Collections.Generic;
using ThIDE.ViewModels;
using Xunit;

namespace ThIDE.Tests;

public class AutoOpenOverrideTests
{
    private static readonly IReadOnlyDictionary<string, bool> None = new Dictionary<string, bool>();

    private static IReadOnlyList<string> Resolve(
        IReadOnlyList<string> paths, bool lox = false, bool d3 = false, bool pdf = false, bool all = false,
        IReadOnlyDictionary<string, bool>? overrides = null)
        => BuildViewModel.ResolveAutoOpenPaths(paths, lox, d3, pdf, all, overrides ?? None);

    [Fact]
    public void Defaults_open_only_enabled_types_first_only()
    {
        var paths = new[] { @"C:\p\a.lox", @"C:\p\b.3d", @"C:\p\c.pdf" };
        var open = Resolve(paths, lox: true, d3: true, pdf: false, all: false);
        Assert.Equal(new[] { @"C:\p\a.lox" }, open);   // first matching default only
    }

    [Fact]
    public void OpenAll_opens_every_enabled_default()
    {
        var paths = new[] { @"C:\p\a.lox", @"C:\p\b.3d", @"C:\p\c.pdf" };
        var open = Resolve(paths, lox: true, d3: true, pdf: false, all: true);
        Assert.Equal(new[] { @"C:\p\a.lox", @"C:\p\b.3d" }, open);
    }

    [Fact]
    public void Explicit_true_opens_even_when_type_default_is_off()
    {
        var paths = new[] { @"C:\p\a.lox", @"C:\p\notes.svg" };
        var overrides = new Dictionary<string, bool> { [@"C:\p\notes.svg"] = true };
        var open = Resolve(paths, lox: false, overrides: overrides);
        Assert.Equal(new[] { @"C:\p\notes.svg" }, open);
    }

    [Fact]
    public void Explicit_false_suppresses_a_type_default()
    {
        var paths = new[] { @"C:\p\a.lox", @"C:\p\b.lox" };
        var overrides = new Dictionary<string, bool> { [@"C:\p\a.lox"] = false };
        var open = Resolve(paths, lox: true, all: true, overrides: overrides);
        Assert.Equal(new[] { @"C:\p\b.lox" }, open);   // a.lox suppressed
    }

    [Fact]
    public void Explicit_true_is_listed_before_defaults_and_deduplicated()
    {
        var paths = new[] { @"C:\p\a.lox", @"C:\p\b.pdf" };
        var overrides = new Dictionary<string, bool> { [@"C:\p\b.pdf"] = true };
        var open = Resolve(paths, lox: true, pdf: true, all: true, overrides: overrides);
        Assert.Equal(new[] { @"C:\p\b.pdf", @"C:\p\a.lox" }, open);   // explicit first, no dupes
    }

    [Fact]
    public void Override_match_is_case_insensitive_and_path_normalized()
    {
        // Root per-OS so Path.GetFullPath collapses the "sub\.." segment on every platform; the
        // upper-cased override key proves the lookup folds case (a C:\ path isn't rooted on *nix).
        var root = System.OperatingSystem.IsWindows() ? @"C:\p" : "/p";
        var artifact = System.IO.Path.Combine(root, "sub", "..", "a.lox");
        var overrideKey = System.IO.Path.Combine(root.ToUpperInvariant(), "A.LOX");
        var overrides = new Dictionary<string, bool> { [overrideKey] = false };
        Assert.Empty(Resolve(new[] { artifact }, lox: true, all: true, overrides: overrides));
    }
}
