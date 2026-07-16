// Built-in layout presets (View ▸ Layout). Pure functions: available tools (+ screen
// geometry for the multi-monitor preset) in, a LayoutProfile out — the shell applies it
// through the same rebuild pipeline as a loaded layout file. No Avalonia types here so
// the shapes are unit-testable.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ThIDE.Services;

/// <summary>A monitor's working area: position in physical pixels (what Avalonia's
/// <c>Window.Position</c> takes), size in logical units (what <c>Window.Width/Height</c> take).</summary>
public sealed record ScreenRect(double X, double Y, double Width, double Height);

public static class LayoutPresets
{
    // Panels that benefit from their own monitor (rendered previews), in dispersal order.
    private static readonly string[] FloatCandidates =
        { "LivePreview", "MapViewer", "Model3DViewer", "StructuralPlot", "StructuralStereonet" };

    /// <summary>
    /// Splits the lateral docking areas into <paramref name="parts"/> (2 or 3) stacked
    /// sections so more panels are visible at once. The 3-way split also divides the
    /// bottom area in two side-by-side sections.
    /// </summary>
    public static LayoutProfile SplitSideRails(IReadOnlySet<string> availableTools, int parts)
    {
        parts = Math.Clamp(parts, 2, 3);

        var leftPool = Keep(availableTools, "Workspace", "Project");
        var rightPool = Keep(availableTools, "Outline", "LivePreview", "MapViewer", "Model3DViewer");

        var bottom = parts >= 3
            ? new[]
            {
                Section(Keep(availableTools, "Diagnostics", "CompilerOutput"), 0.5),
                Section(Keep(availableTools, "GeneratedFiles", "Log"), 0.5),
            }
            : new[] { Section(Keep(availableTools, "Diagnostics", "CompilerOutput", "GeneratedFiles", "Log"), 0) };

        return new LayoutProfile
        {
            // Split rails need a little more width than the single-dock default.
            LeftProportion = 0.20,
            RightProportion = 0.24,
            CenterProportion = 0.56,
            LeftSections = Chunk(leftPool, Math.Min(parts, 2)),
            RightSections = Chunk(rightPool, parts),
            BottomSections = bottom.Where(s => s.Tools.Count > 0).ToArray(),
            CenterTools = Keep(availableTools, "ObjectBrowser"),
        };
    }

    /// <summary>
    /// Disperses the preview panels onto the extra monitors as maximized float windows:
    /// with three or more monitors they spread over monitors 2 and 3; with exactly two
    /// they all land on monitor 2. Returns null when there is only one monitor (or no
    /// preview panel is enabled) — the caller notifies the user instead.
    /// </summary>
    public static LayoutProfile? MultiMonitor(IReadOnlyList<ScreenRect> screens, IReadOnlySet<string> availableTools)
    {
        if (screens.Count < 2) return null;
        var pool = Keep(availableTools, FloatCandidates);
        if (pool.Count == 0) return null;

        // Up to two extra monitors host the floats, one tabbed window per monitor.
        var targets = screens.Skip(1).Take(2).ToList();
        var groups = Chunk(pool, Math.Min(targets.Count, pool.Count));
        var floats = new List<FloatWindowState>();
        for (int i = 0; i < groups.Count; i++)
        {
            var screen = targets[i];
            floats.Add(new FloatWindowState
            {
                X = screen.X,
                Y = screen.Y,
                Width = screen.Width,
                Height = screen.Height,
                Maximized = true,
                DockableIds = groups[i].Tools,
            });
        }

        return new LayoutProfile
        {
            LeftSections = Chunk(Keep(availableTools, "Workspace", "Project"), 2),
            // Everything float-worthy is on the other monitors; the rail keeps the outline.
            RightSections = Chunk(Keep(availableTools, "Outline"), 1),
            RightProportion = 0.16,
            CenterProportion = 0.64,
            BottomSections = new[]
            {
                Section(Keep(availableTools, "Diagnostics", "CompilerOutput", "GeneratedFiles", "Log"), 0),
            },
            CenterTools = Keep(availableTools, "ObjectBrowser"),
            FloatWindows = floats,
        };
    }

    private static List<string> Keep(IReadOnlySet<string> available, params string[] ids) =>
        ids.Where(available.Contains).ToList();

    private static DockSectionState Section(IReadOnlyList<string> tools, double proportion) =>
        new() { Tools = tools, Proportion = proportion };

    /// <summary>Distributes <paramref name="pool"/> over up to <paramref name="parts"/> equal
    /// sections, preserving order (never returns an empty section).</summary>
    private static List<DockSectionState> Chunk(IReadOnlyList<string> pool, int parts)
    {
        var result = new List<DockSectionState>();
        if (pool.Count == 0) return result;
        parts = Math.Clamp(parts, 1, pool.Count);
        int baseSize = pool.Count / parts, remainder = pool.Count % parts;
        int index = 0;
        for (int i = 0; i < parts; i++)
        {
            int size = baseSize + (i < remainder ? 1 : 0);
            result.Add(new DockSectionState
            {
                Tools = pool.Skip(index).Take(size).ToList(),
                Proportion = 1.0 / parts,
            });
            index += size;
        }
        return result;
    }
}
