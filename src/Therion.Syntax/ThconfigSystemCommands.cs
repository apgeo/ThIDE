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

    /// <summary>The raw path tokens of a thconfig's <c>input</c> lines: the config fragments it pulls in.</summary>
    public static IEnumerable<string> ConfigInputs(TherionFile thconfig)
    {
        if (thconfig.Children.IsDefaultOrEmpty) yield break;
        foreach (var node in thconfig.Children)
            if (node is UnknownCommand cmd && string.Equals(cmd.Keyword, "input", StringComparison.OrdinalIgnoreCase)
                && cmd.RawArguments.Trim().Trim('"') is { Length: > 0 } token)
                yield return token;
    }
}
