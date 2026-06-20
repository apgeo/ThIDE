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

            int seen = 0;
            int topLevelHits = 0;
            foreach (var tok in tokens)
            {
                if (seen++ >= SnifferConstants.SnifferMaxTokens) break;
                if (tok.Kind != TherionTokenKind.Identifier) continue;
                if (ThconfigParser.TopLevelKeywords.Contains(tok.Text)) topLevelHits++;
                if (topLevelHits >= 2) return SnifferVerdict.Likely;
            }

            return topLevelHits >= 1 ? SnifferVerdict.Likely : SnifferVerdict.Unlikely;
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
