// Shared parse+bind pipeline for a single file. Extracted so both the
// multi-document FileDocumentViewModel and the workspace path produce a model
// the same way (extension dispatch mirrors the old DocumentService.OpenFileAsync).

using System.Collections.Immutable;
using System.IO;
using Therion.Core;
using Therion.Processing.Abstractions;
using Therion.Semantics;
using Therion.Syntax;

namespace TherionProc.Services;

/// <summary>Result of parsing + binding one source file.</summary>
public sealed record ParsedDocument(
    TherionFile? Ast,
    SemanticModel? Semantics,
    ISymbolNavigationService? Navigation,
    ImmutableArray<Diagnostic> Diagnostics);

public static class DocumentParser
{
    /// <summary>Parses a file's text into an AST, dispatching on its extension.</summary>
    public static ParseResult<TherionFile> ParseByExtension(
        string filePath, string text, ICommandRegistry? commands = null)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".th2"      => new Th2Parser().Parse(filePath, text),
            ".thconfig" => new ThconfigParser().Parse(filePath, text),
            ".thc"      => new ThconfigParser().Parse(filePath, text),
            _           => new ThParser(commands).Parse(filePath, text),
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

        return new ParsedDocument(parsed.Value, semantics, navigation, diags.ToImmutable());
    }
}
