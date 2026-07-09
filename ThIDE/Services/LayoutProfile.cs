// Full-layout persistence — the declarative "layout profile".
//
// Dock.Avalonia 12 cannot render a DESERIALIZED dock tree (tab strips appear, content
// stays blank — see DockFactory.PersistDockLayout). What does render is a tree BUILT
// programmatically plus live float operations — exactly how BuildDefaultLayout and
// RestoreFloatWindows already work. So instead of serializing the tree we persist this
// plain description of it (which tool sits in which dock section, section order/active
// tab/proportions, float-window bounds) and REBUILD the layout from it on load.
//
// The same record backs: the continuous autosave (LayoutState.Profile), the user-saved
// layout files (View ▸ Layout ▸ Save/Load, *.thlayout), and the built-in presets.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace ThIDE.Services;

/// <summary>One tool dock in a rail: its tabs (tool ids, in order), the selected tab and
/// its share of the rail (0 = unset → divided evenly).</summary>
public sealed record DockSectionState
{
    public IReadOnlyList<string> Tools { get; init; } = Array.Empty<string>();
    public string? ActiveTool { get; init; }
    public double Proportion { get; init; }
}

/// <summary>
/// A complete, declarative description of the shell layout. Version 1.
/// Left/right rails hold 1..n vertically-stacked sections; the bottom area holds 1..n
/// side-by-side sections; <see cref="CenterTools"/> are tools docked in the document well
/// (e.g. the Object Browser). Floated windows carry their bounds + member dockable ids.
/// </summary>
public sealed record LayoutProfile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    // Main-row shares (0 = unset → the defaults used by the fresh layout).
    public double LeftProportion { get; init; } = 0.18;
    public double RightProportion { get; init; } = 0.22;
    public double BottomProportion { get; init; } = 0.28;
    public double CenterProportion { get; init; } = 0.60;

    public IReadOnlyList<DockSectionState> LeftSections { get; init; } = Array.Empty<DockSectionState>();
    public IReadOnlyList<DockSectionState> RightSections { get; init; } = Array.Empty<DockSectionState>();
    public IReadOnlyList<DockSectionState> BottomSections { get; init; } = Array.Empty<DockSectionState>();
    public IReadOnlyList<string> CenterTools { get; init; } = Array.Empty<string>();
    public string? CenterActiveTool { get; init; }

    public IReadOnlyList<FloatWindowState> FloatWindows { get; init; } = Array.Empty<FloatWindowState>();

    /// <summary>Every tool id the profile places somewhere (docked or floated).</summary>
    public IEnumerable<string> AllToolIds()
    {
        foreach (var s in LeftSections.Concat(RightSections).Concat(BottomSections))
            foreach (var t in s.Tools) yield return t;
        foreach (var t in CenterTools) yield return t;
        foreach (var w in FloatWindows)
            foreach (var t in w.DockableIds) yield return t;
    }
}

/// <summary>Save/load a <see cref="LayoutProfile"/> as a standalone *.thlayout JSON file.</summary>
public static class LayoutProfileFile
{
    public const string Extension = ".thlayout";

    public static void Save(string path, LayoutProfile profile)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path,
            JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Null on any error or on a newer (unknown) version — the caller notifies.</summary>
    public static LayoutProfile? TryLoad(string path)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<LayoutProfile>(File.ReadAllText(path));
            return profile is { Version: <= LayoutProfile.CurrentVersion and >= 1 } ? profile : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Reads a live dock tree into a <see cref="LayoutProfile"/>. Pure model traversal (no UI),
/// so it is unit-testable with hand-built trees. Regions are located relative to the
/// document well: main-row children before the subtree containing it are the left rail,
/// after it the right rail; tool docks inside that subtree are the bottom area. Exotic
/// drag-created shapes degrade gracefully — every tool dock still lands in some region.
/// </summary>
public static class LayoutProfileCapture
{
    public static LayoutProfile Capture(IRootDock root, Func<string, bool> isKnownTool)
    {
        var left = new List<DockSectionState>();
        var right = new List<DockSectionState>();
        var bottom = new List<DockSectionState>();
        var center = new List<string>();
        string? centerActive = null;
        double leftProp = 0, rightProp = 0, bottomProp = 0, centerProp = 0;

        // The main row: the first child of the root whose subtree holds the document well.
        var main = root.VisibleDockables?.FirstOrDefault(ContainsDocumentDock)
                   ?? root.VisibleDockables?.FirstOrDefault();

        if (main is IDock mainRow && mainRow.VisibleDockables is { } rowChildren && ContainsDocumentDock(main))
        {
            bool seenCenter = false;
            foreach (var child in rowChildren)
            {
                if (child is IProportionalDockSplitter) continue;
                if (!seenCenter && ContainsDocumentDock(child))
                {
                    seenCenter = true;
                    centerProp = Sanitize(child.Proportion);
                    CaptureCenter(child, isKnownTool, bottom, center, ref centerActive, ref bottomProp);
                }
                else if (!seenCenter)
                {
                    if (leftProp <= 0) leftProp = Sanitize(child.Proportion);
                    CollectSections(child, isKnownTool, left);
                }
                else
                {
                    if (rightProp <= 0) rightProp = Sanitize(child.Proportion);
                    CollectSections(child, isKnownTool, right);
                }
            }
        }
        else if (main is not null)
        {
            // Unrecognized shape — capture whatever tool docks exist into the center region.
            CaptureCenter(main, isKnownTool, bottom, center, ref centerActive, ref bottomProp);
        }

        var defaults = new LayoutProfile();
        return new LayoutProfile
        {
            LeftProportion = leftProp > 0 ? leftProp : defaults.LeftProportion,
            RightProportion = rightProp > 0 ? rightProp : defaults.RightProportion,
            BottomProportion = bottomProp > 0 ? bottomProp : defaults.BottomProportion,
            CenterProportion = centerProp > 0 ? centerProp : defaults.CenterProportion,
            LeftSections = left,
            RightSections = right,
            BottomSections = bottom,
            CenterTools = center,
            CenterActiveTool = centerActive,
        };
    }

    /// <summary>The center column: document-well tools → CenterTools; tool docks → bottom.</summary>
    private static void CaptureCenter(
        IDockable node, Func<string, bool> isKnownTool,
        List<DockSectionState> bottom, List<string> center, ref string? centerActive,
        ref double bottomProp)
    {
        if (node is IDocumentDock well)
        {
            if (well.VisibleDockables is { } tabs)
                foreach (var t in tabs)
                    if (t is ITool && t.Id is { } id && isKnownTool(id) && !center.Contains(id))
                        center.Add(id);
            if (well.ActiveDockable is ITool { Id: { } activeId } && isKnownTool(activeId))
                centerActive = activeId;
            return;
        }
        if (node is IToolDock)
        {
            if (bottomProp <= 0) bottomProp = Sanitize(node.Proportion);
            CollectSections(node, isKnownTool, bottom);
            return;
        }
        if (node is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (var child in children)
            {
                if (child is IProportionalDockSplitter) continue;
                // A center-column child that holds no document well is the bottom area —
                // its proportion is the bottom height, whether it is a bare tool dock or
                // a horizontal wrapper of several sections.
                if (!ContainsDocumentDock(child) && bottomProp <= 0 && child is IDock)
                    bottomProp = Sanitize(child.Proportion);
                CaptureCenter(child, isKnownTool, bottom, center, ref centerActive, ref bottomProp);
            }
        }
    }

    private static void CollectSections(IDockable node, Func<string, bool> isKnownTool, List<DockSectionState> into)
    {
        if (node is IToolDock td)
        {
            var tools = new List<string>();
            if (td.VisibleDockables is { } tabs)
                foreach (var t in tabs)
                    if (t is ITool && t.Id is { } id && isKnownTool(id) && !tools.Contains(id))
                        tools.Add(id);
            if (tools.Count > 0)
                into.Add(new DockSectionState
                {
                    Tools = tools,
                    ActiveTool = td.ActiveDockable?.Id is { } aid && tools.Contains(aid) ? aid : null,
                    Proportion = Sanitize(td.Proportion),
                });
            return;
        }
        if (node is IDock dock && dock.VisibleDockables is { } children)
            foreach (var child in children)
                CollectSections(child, isKnownTool, into);
    }

    private static bool ContainsDocumentDock(IDockable node)
    {
        if (node is IDocumentDock) return true;
        if (node is IDock dock && dock.VisibleDockables is { } children)
            foreach (var c in children)
                if (ContainsDocumentDock(c)) return true;
        return false;
    }

    // Dock uses NaN for "unset" proportions; NaN is not JSON-serializable, so store 0.
    private static double Sanitize(double proportion) =>
        double.IsNaN(proportion) || double.IsInfinity(proportion) || proportion < 0 ? 0 : proportion;
}
