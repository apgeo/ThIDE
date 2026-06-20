// Implementation Plan §7.3 / §7.5 — model edit service.
// Validates inline edits and applies them via ITherionWriter (round-trip).
// Backs the Object Browser DataGrid + Properties panel.

using System.Collections.Immutable;
using Therion.Core;
using Therion.Syntax;

namespace Therion.Semantics;

/// <summary>Outcome of an inline edit attempt.</summary>
public sealed record EditResult(
    bool Success,
    string? UpdatedText,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Applies single-node edits to an in-memory <see cref="TherionFile"/> and
/// re-serializes the file via <see cref="ITherionWriter"/>. The result is the
/// new file text; persistence is the caller's responsibility (workspace layer).
/// </summary>
public interface IModelEditService
{
    /// <summary>Replace <paramref name="original"/> with <paramref name="replacement"/> in <paramref name="file"/>.</summary>
    EditResult ReplaceNode(TherionFile file, TherionNode original, TherionNode replacement);
}

public sealed class ModelEditService : IModelEditService
{
    private readonly ITherionWriter _writer;

    public ModelEditService(ITherionWriter? writer = null)
    {
        _writer = writer ?? new TherionWriter();
    }

    public EditResult ReplaceNode(TherionFile file, TherionNode original, TherionNode replacement)
    {
        if (original.GetType() != replacement.GetType())
        {
            return new EditResult(false, null, ImmutableArray.Create(
                Diagnostic.Create("TH_EDIT_001", DiagnosticSeverity.Error,
                    $"Cannot replace node of type {original.GetType().Name} with {replacement.GetType().Name}.",
                    original.Span)));
        }

        var (updatedChildren, found) = ReplaceIn(file.Children, original, replacement);
        if (!found)
        {
            return new EditResult(false, null, ImmutableArray.Create(
                Diagnostic.Create("TH_EDIT_002", DiagnosticSeverity.Error,
                    "Target node not found in the file.", original.Span)));
        }

        var updatedFile = file with { Children = updatedChildren };
        var text = _writer.Write(updatedFile);
        return new EditResult(true, text, ImmutableArray<Diagnostic>.Empty);
    }

    private static (ImmutableArray<TherionNode> Children, bool Found) ReplaceIn(
        ImmutableArray<TherionNode> children, TherionNode original, TherionNode replacement)
    {
        var builder = ImmutableArray.CreateBuilder<TherionNode>(children.Length);
        bool found = false;
        foreach (var child in children)
        {
            if (ReferenceEquals(child, original))
            {
                builder.Add(replacement);
                found = true;
            }
            else if (child is BlockCommand block)
            {
                var (innerChildren, innerFound) = ReplaceIn(block.Children, original, replacement);
                if (innerFound)
                {
                    builder.Add(block switch
                    {
                        SurveyCommand s     => s with { Children = innerChildren },
                        CentrelineCommand c => c with { Children = innerChildren },
                        ScrapBlock sc       => sc with { Children = innerChildren },
                        _ => block,
                    });
                    found = true;
                }
                else builder.Add(child);
            }
            else builder.Add(child);
        }
        return (builder.ToImmutable(), found);
    }
}
