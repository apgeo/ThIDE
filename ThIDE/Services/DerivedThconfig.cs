// Writes the temporary thconfig that single-target, quick-export and partial-batch builds hand to
// Therion: the source thconfig run through an editor transform, kept in the source's own encoding
// (its `encoding` line is preserved, so a UTF-8 rewrite would garble non-ASCII text).

using System;
using System.IO;
using Therion.Workspace.Editing;

namespace ThIDE.Services;

internal static class DerivedThconfig
{
    /// <summary>Reads <paramref name="source"/> as Therion would, applies <paramref name="transform"/>
    /// and writes the result to <paramref name="temp"/> in the same encoding.</summary>
    public static void Write(string source, string temp, Func<string, string> transform)
    {
        var file = SourceFileIo.Read(source);
        var (bytes, _) = SourceFileIo.EncodeForSave(temp, transform(file.Text), SourceFileIo.FormatOf(file));
        File.WriteAllBytes(temp, bytes);
    }
}
