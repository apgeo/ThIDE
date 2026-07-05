// Shared parse+bind pipeline for a single file. Extracted so both the
// multi-document FileDocumentViewModel and the workspace path produce a model
// the same way (extension dispatch mirrors the old DocumentService.OpenFileAsync).

using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace ThIDE.Services;

/// <summary>Result of parsing + binding one source file.</summary>
public sealed record ParsedDocument(
    TherionFile? Ast,
    SemanticModel? Semantics,
    ISymbolNavigationService? Navigation,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>How a file is interpreted by the editor (#5): drives parsing + the footer label.</summary>
public enum SourceFileType { Thconfig, Th, Th2, PlainText }

public static class DocumentParser
{
    /// <summary>
    /// Classifies a file by extension (and, for extensionless files, a cheap content sniff)
    /// into an interpreted type and whether the editor should parse/syntax-check it. Only
    /// Therion source files are parsed; .log/.txt/etc. are treated as plain text (#5).
    /// </summary>
    public static (SourceFileType Type, bool Parseable) Classify(string filePath, string text)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        switch (ext)
        {
            case ".thconfig":
            case ".thc": return (SourceFileType.Thconfig, true);
            case ".th2": return (SourceFileType.Th2, true);
            case ".th":  return (SourceFileType.Th, true);
            case "":
                // Extensionless files are Therion config only when their content looks like one
                // (the common "thconfig" entry has no extension); otherwise leave them unparsed.
                return LooksLikeThconfig(text)
                    ? (SourceFileType.Thconfig, true)
                    : (SourceFileType.PlainText, false);
            default: return (SourceFileType.PlainText, false);
        }
    }

    /// <summary>Human-readable label for the status bar, e.g. "Therion survey (.th)" (#5).</summary>
    public static string DescribeType(string filePath, SourceFileType type)
    {
        var ext = Path.GetExtension(filePath);
        var extLabel = string.IsNullOrEmpty(ext) ? "no extension" : ext;
        var name = type switch
        {
            SourceFileType.Thconfig => "Therion configuration",
            SourceFileType.Th       => "Therion survey",
            SourceFileType.Th2      => "Therion sketch",
            _                       => "Plain text",
        };
        return $"{name} ({extLabel})";
    }

    // Cheap heuristic for extensionless files: a top-level thconfig directive near the top.
    private static readonly string[] ThconfigMarkers =
        { "source", "layout", "endlayout", "export", "select", "system-charset", "encoding" };

    private static bool LooksLikeThconfig(string text)
    {
        int scanned = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var first = line.Split(new[] { ' ', '\t' }, 2)[0].ToLowerInvariant();
            foreach (var m in ThconfigMarkers)
                if (first == m) return true;
            if (++scanned >= 40) break; // only probe the head of the file
        }
        return false;
    }

    /// <summary>Parses a file's text into an AST, dispatching on its interpreted type.</summary>
    public static ParseResult<TherionFile> ParseByExtension(
        string filePath, string text, ICommandRegistry? commands = null)
    {
        var (type, _) = Classify(filePath, text);
        return type switch
        {
            SourceFileType.Th2      => new Th2Parser().Parse(filePath, text),
            SourceFileType.Thconfig => new ThconfigParser().Parse(filePath, text),
            _                       => new ThParser(commands).Parse(filePath, text),
        };
    }

    public static ParsedDocument Parse(string filePath, string text, ICommandRegistry? commands = null)
    {
        var parsed = ParseByExtension(filePath, text, commands);

        var semantics = parsed.Value is null ? null : new SemanticBinder().Bind(parsed.Value);
        var navigation = semantics is null ? null : new SymbolNavigationService(semantics);

        var diags = ImmutableArray.CreateBuilder<Diagnostic>();
        diags.AddRange(parsed.Diagnostics);
        if (semantics is not null) diags.AddRange(semantics.Diagnostics);
        // Application directives (`#@region`…) live in comments — scan for their warnings.
        diags.AddRange(Therion.Syntax.Directives.DirectiveScanner.Scan(text, filePath).Diagnostics);

        return new ParsedDocument(parsed.Value, semantics, navigation, diags.ToImmutable());
    }
}
