// Implementation Plan §4.2 — host-level ambient parser options.
// The composition root or the UI may overwrite Current to flip lenient/strict
// at runtime. Parsers that don't take ParserOptions explicitly read from here.

namespace Therion.Syntax;

public static class ParserOptionsHost
{
    public static ParserOptions Current { get; set; } = ParserOptions.Default;
}
