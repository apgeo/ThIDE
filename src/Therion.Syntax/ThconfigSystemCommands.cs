// A thconfig's `system` lines are shell commands Therion runs while it compiles (thbook
// §"Processing data"). Anything that starts a build on someone else's behalf — an agent's run_build —
// has to know whether the build would run commands before it starts it; this finds them.

using System;
using System.Collections.Generic;
using Therion.Core;

namespace Therion.Syntax;

public static class ThconfigSystemCommands
{
    /// <summary>The command text of every top-level <c>system</c> line in a parsed thconfig, in order.</summary>
    public static IEnumerable<(string Command, SourceSpan Span)> Find(TherionFile thconfig)
    {
        if (thconfig.Children.IsDefaultOrEmpty) yield break;
        foreach (var node in thconfig.Children)
            if (node is UnknownCommand cmd && string.Equals(cmd.Keyword, "system", StringComparison.OrdinalIgnoreCase))
                yield return (cmd.RawArguments.Trim(), cmd.Span);
    }

    /// <summary>
    /// The path tokens of a thconfig's <c>input</c> lines: the config fragments it pulls in. The raw
    /// arguments run to the end of the line, so a trailing <c># comment</c> is cut off here and only the
    /// first word (quotes honoured) is the path. A bare word that holds a <c>#</c> yields both readings,
    /// the whole word and the part before the <c>#</c>: a caller that skips names it cannot find then
    /// still scans the fragment, whichever one the compiler reads.
    /// </summary>
    public static IEnumerable<string> ConfigInputs(TherionFile thconfig)
    {
        if (thconfig.Children.IsDefaultOrEmpty) yield break;
        foreach (var node in thconfig.Children)
        {
            if (node is not UnknownCommand cmd || !string.Equals(cmd.Keyword, "input", StringComparison.OrdinalIgnoreCase))
                continue;
            var raw = cmd.RawArguments.TrimStart();
            if (raw.Length == 0 || raw[0] == '#') continue;
            if (raw[0] == '"')
            {
                int close = raw.IndexOf('"', 1);
                var quoted = close < 0 ? raw[1..] : raw[1..close];
                if (quoted.Length > 0) yield return quoted;
                continue;
            }
            int end = 0;
            while (end < raw.Length && !char.IsWhiteSpace(raw[end])) end++;
            var word = raw[..end];
            yield return word;
            if (word.IndexOf('#') is > 0 and var hash) yield return word[..hash];
        }
    }
}
