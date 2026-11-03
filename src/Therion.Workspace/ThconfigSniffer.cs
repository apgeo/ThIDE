// Implementation Plan §6.1 — "is-this-a-thconfig?" probe.
// Conservative: never throws; returns Unknown on any failure.

using Therion.Processing.Abstractions;
using Therion.Syntax;

namespace Therion.Workspace;

public sealed class ThconfigSniffer : IThconfigSniffer
{
    private readonly TherionTokenizer _tokenizer = new();

    public SnifferVerdict Probe(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return SnifferVerdict.Unknown;
            if (info.Length > SnifferConstants.SnifferMaxFileSizeBytes) return SnifferVerdict.Unknown;

            using (var stream = File.OpenRead(filePath))
            {
                Span<byte> probe = stackalloc byte[SnifferConstants.SnifferBinaryProbeBytes];
                int read = stream.Read(probe);
                if (LooksBinary(probe[..read])) return SnifferVerdict.Unlikely;
            }

            var text = File.ReadAllText(filePath);
            var tokens = _tokenizer.Tokenize(filePath, text);

            // Only a keyword in command position (the first token of a logical line) counts, and
            // two distinct ones are needed: `source`, `input`, `encoding`, `text` or `log` alone are
            // ordinary words in prose and ordinary commands in survey files.
            int seen = 0;
            bool atLineStart = true;
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in tokens)
            {
                if (seen++ >= SnifferConstants.SnifferMaxTokens) break;
                switch (tok.Kind)
                {
                    case TherionTokenKind.NewLine:
                        atLineStart = true;
                        continue;
                    case TherionTokenKind.Whitespace:
                    case TherionTokenKind.LineContinuation:
                    case TherionTokenKind.LineComment:
                        continue;
                }

                if (atLineStart && tok.Kind == TherionTokenKind.Identifier &&
                    ThconfigParser.TopLevelKeywords.Contains(tok.Text) &&
                    keywords.Add(tok.Text) && keywords.Count >= 2)
                    return SnifferVerdict.Likely;
                atLineStart = false;
            }

            return SnifferVerdict.Unlikely;
        }
        catch
        {
            return SnifferVerdict.Unknown;
        }
    }

    private static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return false;
        int controls = 0;
        foreach (var b in bytes)
        {
            if (b == 0) return true;
            if (b < 0x09 || (b > 0x0D && b < 0x20)) controls++;
        }
        return controls * 10 > bytes.Length; // > 10% control chars ? binary
    }
}
