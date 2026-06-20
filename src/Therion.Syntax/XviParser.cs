// Implementation Plan §4 — XVI parser.
// Strict-by-design: XVI is a fixed-structure machine-generated format.
// Missing keywords emit diagnostics but the parser still returns a partial
// XviFile so downstream code can attempt resolution.

using System.Collections.Immutable;
using System.Globalization;
using Therion.Core;

namespace Therion.Syntax;

public sealed class XviParser
{
    public ParseResult<XviFile> Parse(string filePath, string text, ParserOptions? options = null)
    {
        options ??= ParserOptions.Default;
        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        var (lines, leadingComments) = XviTokenizer.Tokenize(filePath, text);

        int version = 0;
        double scale = 0;
        AffineTransform2D xform = new(1, 0, 0, 1, 0, 0);
        string image = string.Empty;
        var calib = ImmutableArray.CreateBuilder<CalibrationPoint>();
        SourceSpan fileSpan = lines.Length == 0
            ? new SourceSpan(filePath, SourceLocation.Start, SourceLocation.Start, 0, text.Length)
            : new SourceSpan(filePath, lines[0].Span.Start, lines[^1].Span.End,
                lines[0].Span.StartOffset,
                lines[^1].Span.StartOffset + lines[^1].Span.Length - lines[0].Span.StartOffset);

        bool sawVersion = false, sawTransform = false, sawImage = false;

        foreach (var ln in lines)
        {
            switch (ln.Keyword.ToUpperInvariant())
            {
                case "XVI":
                    sawVersion = true;
                    version = ln.Args.Length > 0 && int.TryParse(ln.Args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
                    break;

                case "SCALE":
                    if (ln.Args.Length < 1 || !double.TryParse(ln.Args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                    {
                        diags.Add(Diagnostic.Create(DiagnosticCodes.XviMalformedScale,
                            DiagnosticSeverity.Warning,
                            "Malformed SCALE — expected a single numeric value.", ln.Span));
                        scale = 0;
                    }
                    break;

                case "TRANSFORM":
                case "XVITRANS":
                    if (ln.Args.Length < 6)
                    {
                        diags.Add(Diagnostic.Create(DiagnosticCodes.XviMalformedTransform,
                            DiagnosticSeverity.Warning,
                            "TRANSFORM requires 6 numeric values (a b c d tx ty).", ln.Span));
                    }
                    else if (TryParseAll(ln.Args, 0, 6, out var vals))
                    {
                        xform = new AffineTransform2D(vals[0], vals[1], vals[2], vals[3], vals[4], vals[5]);
                        sawTransform = true;
                    }
                    else
                    {
                        diags.Add(Diagnostic.Create(DiagnosticCodes.XviMalformedTransform,
                            DiagnosticSeverity.Warning,
                            "TRANSFORM contains non-numeric values.", ln.Span));
                    }
                    break;

                case "IMAGE":
                    if (ln.Args.Length < 1)
                    {
                        diags.Add(Diagnostic.Create(DiagnosticCodes.XviMissingImage,
                            DiagnosticSeverity.Warning,
                            "IMAGE keyword requires a path.", ln.Span));
                    }
                    else
                    {
                        image = string.Join(' ', ln.Args);
                        sawImage = true;
                    }
                    break;

                case "CALIBRATION":
                    for (int i = 0; i + 3 < ln.Args.Length; i += 4)
                    {
                        if (TryParseAll(ln.Args, i, 4, out var c))
                            calib.Add(new CalibrationPoint(c[0], c[1], c[2], c[3]));
                    }
                    break;

                default:
                    // Unknown keyword: lenient ? warning, strict ? error.
                    var sev = options.Mode == ParserMode.Strict
                        ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                    diags.Add(Diagnostic.Create(DiagnosticCodes.XviUnknownKeyword, sev,
                        $"Unknown XVI keyword '{ln.Keyword}'.", ln.Span));
                    break;
            }
        }

        if (!sawVersion)
            diags.Add(Diagnostic.Create(DiagnosticCodes.XviMissingVersion,
                DiagnosticSeverity.Warning, "Missing XVI <version> header.", fileSpan));
        if (!sawTransform)
            diags.Add(Diagnostic.Create(DiagnosticCodes.XviMalformedTransform,
                DiagnosticSeverity.Warning, "Missing TRANSFORM line.", fileSpan));
        if (!sawImage)
            diags.Add(Diagnostic.Create(DiagnosticCodes.XviMissingImage,
                DiagnosticSeverity.Warning, "Missing IMAGE line.", fileSpan));

        var file = new XviFile(fileSpan, filePath, version, scale, xform, image,
            calib.ToImmutable(), leadingComments);
        return new ParseResult<XviFile>(file, diags.ToImmutable());
    }

    private static bool TryParseAll(ImmutableArray<string> args, int start, int count, out double[] values)
    {
        values = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (!double.TryParse(args[start + i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }
        return true;
    }
}
