// Section B (EDIT-*) editor-feature gating. Every EDIT feature is toggleable two ways:
//   1. a compile-time master constant in EditorFeatureFlags (set false to compile it out), and
//   2. a per-feature runtime switch in AppSettings.EditorFeatures (the Preferences "Editor
//      Features" section).
// The effective state is the AND of the two, exposed through EditorFeatureFlags.IsEnabled().
// EditorFeatureCatalog supplies the human-readable code/title/description used by the UI.

using System;
using System.Collections.Generic;

namespace TherionProc.Services;

/// <summary>The Section-B editor features that can be individually enabled/disabled.</summary>
public enum EditorFeature
{
    RichCompletion,     // EDIT-01
    SignatureHelp,      // EDIT-02
    Snippets,           // EDIT-03
    FormatDocument,     // EDIT-04
    Minimap,            // EDIT-07
    StickyScroll,       // EDIT-08
    Outline,            // EDIT-09
    PeekDefinition,     // EDIT-10
    SplitView,          // EDIT-11
    ColorAdornments,    // EDIT-12
    WhitespaceGuides,   // EDIT-13
    TrimTrailingOnSave, // EDIT-14
    MatchTerminator,    // EDIT-15
    SmartEnter,         // EDIT-16
    ReadingOrderNav,    // EDIT-17
}

/// <summary>
/// Compile-time master switches for the EDIT-* features. Flip one to <c>false</c> to remove the
/// feature from the build entirely; the matching runtime setting then has no effect. The
/// effective state of a feature is <c>const &amp;&amp; AppSettings.EditorFeatures.X</c> — see
/// <see cref="IsEnabled(EditorFeature, AppSettings?)"/>.
/// </summary>
public static class EditorFeatureFlags
{
    public const bool RichCompletion     = true; // EDIT-01
    public const bool SignatureHelp      = true; // EDIT-02
    public const bool Snippets           = true; // EDIT-03
    public const bool FormatDocument     = true; // EDIT-04
    public const bool Minimap            = true; // EDIT-07
    public const bool StickyScroll       = true; // EDIT-08
    public const bool Outline            = true; // EDIT-09
    public const bool PeekDefinition     = true; // EDIT-10
    public const bool SplitView          = true; // EDIT-11
    public const bool ColorAdornments    = true; // EDIT-12
    public const bool WhitespaceGuides   = true; // EDIT-13
    public const bool TrimTrailingOnSave = true; // EDIT-14
    public const bool MatchTerminator    = true; // EDIT-15
    public const bool SmartEnter         = true; // EDIT-16
    public const bool ReadingOrderNav    = true; // EDIT-17

    /// <summary>The compile-time master switch for <paramref name="feature"/>.</summary>
    public static bool Compiled(EditorFeature feature) => feature switch
    {
        EditorFeature.RichCompletion     => RichCompletion,
        EditorFeature.SignatureHelp      => SignatureHelp,
        EditorFeature.Snippets           => Snippets,
        EditorFeature.FormatDocument     => FormatDocument,
        EditorFeature.Minimap            => Minimap,
        EditorFeature.StickyScroll       => StickyScroll,
        EditorFeature.Outline            => Outline,
        EditorFeature.PeekDefinition     => PeekDefinition,
        EditorFeature.SplitView          => SplitView,
        EditorFeature.ColorAdornments    => ColorAdornments,
        EditorFeature.WhitespaceGuides   => WhitespaceGuides,
        EditorFeature.TrimTrailingOnSave => TrimTrailingOnSave,
        EditorFeature.MatchTerminator    => MatchTerminator,
        EditorFeature.SmartEnter         => SmartEnter,
        EditorFeature.ReadingOrderNav    => ReadingOrderNav,
        _ => false,
    };

    /// <summary>
    /// The effective state of <paramref name="feature"/>: enabled only when both the compile-time
    /// constant and the user's runtime setting allow it. A null <paramref name="settings"/> falls
    /// back to defaults (all on).
    /// </summary>
    public static bool IsEnabled(EditorFeature feature, AppSettings? settings) =>
        Compiled(feature) && (settings ?? AppSettings.Default).EditorFeatures.IsEnabled(feature);
}

/// <summary>Per-feature runtime toggles, persisted inside <see cref="AppSettings"/> (default: all on).</summary>
public sealed record EditorFeatureSettings
{
    public bool RichCompletion     { get; init; } = true;
    public bool SignatureHelp      { get; init; } = true;
    public bool Snippets           { get; init; } = true;
    public bool FormatDocument     { get; init; } = true;
    public bool Minimap            { get; init; } = true;
    public bool StickyScroll       { get; init; } = true;
    public bool Outline            { get; init; } = true;
    public bool PeekDefinition     { get; init; } = true;
    public bool SplitView          { get; init; } = true;
    public bool ColorAdornments    { get; init; } = true;
    public bool WhitespaceGuides   { get; init; } = true;
    public bool TrimTrailingOnSave { get; init; } = true;
    public bool MatchTerminator    { get; init; } = true;
    public bool SmartEnter         { get; init; } = true;
    public bool ReadingOrderNav    { get; init; } = true;

    /// <summary>The runtime toggle for <paramref name="feature"/>.</summary>
    public bool IsEnabled(EditorFeature feature) => feature switch
    {
        EditorFeature.RichCompletion     => RichCompletion,
        EditorFeature.SignatureHelp      => SignatureHelp,
        EditorFeature.Snippets           => Snippets,
        EditorFeature.FormatDocument     => FormatDocument,
        EditorFeature.Minimap            => Minimap,
        EditorFeature.StickyScroll       => StickyScroll,
        EditorFeature.Outline            => Outline,
        EditorFeature.PeekDefinition     => PeekDefinition,
        EditorFeature.SplitView          => SplitView,
        EditorFeature.ColorAdornments    => ColorAdornments,
        EditorFeature.WhitespaceGuides   => WhitespaceGuides,
        EditorFeature.TrimTrailingOnSave => TrimTrailingOnSave,
        EditorFeature.MatchTerminator    => MatchTerminator,
        EditorFeature.SmartEnter         => SmartEnter,
        EditorFeature.ReadingOrderNav    => ReadingOrderNav,
        _ => false,
    };

    /// <summary>Returns a copy with <paramref name="feature"/> set to <paramref name="value"/>.</summary>
    public EditorFeatureSettings With(EditorFeature feature, bool value) => feature switch
    {
        EditorFeature.RichCompletion     => this with { RichCompletion = value },
        EditorFeature.SignatureHelp      => this with { SignatureHelp = value },
        EditorFeature.Snippets           => this with { Snippets = value },
        EditorFeature.FormatDocument     => this with { FormatDocument = value },
        EditorFeature.Minimap            => this with { Minimap = value },
        EditorFeature.StickyScroll       => this with { StickyScroll = value },
        EditorFeature.Outline            => this with { Outline = value },
        EditorFeature.PeekDefinition     => this with { PeekDefinition = value },
        EditorFeature.SplitView          => this with { SplitView = value },
        EditorFeature.ColorAdornments    => this with { ColorAdornments = value },
        EditorFeature.WhitespaceGuides   => this with { WhitespaceGuides = value },
        EditorFeature.TrimTrailingOnSave => this with { TrimTrailingOnSave = value },
        EditorFeature.MatchTerminator    => this with { MatchTerminator = value },
        EditorFeature.SmartEnter         => this with { SmartEnter = value },
        EditorFeature.ReadingOrderNav    => this with { ReadingOrderNav = value },
        _ => this,
    };
}

/// <summary>Human-readable metadata for each EDIT-* feature (backs the Preferences UI).</summary>
public readonly record struct EditorFeatureInfo(
    EditorFeature Feature, string Code, string Title, string Description);

public static class EditorFeatureCatalog
{
    public static readonly IReadOnlyList<EditorFeatureInfo> All = new[]
    {
        new EditorFeatureInfo(EditorFeature.RichCompletion, "EDIT-01", "Rich completion items",
            "Per-kind icons, documentation detail, and signature/snippet insertion in autocomplete."),
        new EditorFeatureInfo(EditorFeature.SignatureHelp, "EDIT-02", "Parameter / signature help",
            "Shows the expected next field as the caret moves across a command's arguments."),
        new EditorFeatureInfo(EditorFeature.Snippets, "EDIT-03", "Code snippets & templates",
            "Tab-expandable templates (survey block, centreline, scrap, thconfig skeleton, map block)."),
        new EditorFeatureInfo(EditorFeature.FormatDocument, "EDIT-04", "Format document",
            "Normalises block indentation, aligns data columns and flag spelling via the Therion writer."),
        new EditorFeatureInfo(EditorFeature.Minimap, "EDIT-07", "Minimap / code overview",
            "A condensed overview of the whole document beside the scrollbar."),
        new EditorFeatureInfo(EditorFeature.StickyScroll, "EDIT-08", "Sticky scroll & breadcrumbs",
            "Pins the enclosing survey/centreline/scrap header and shows a breadcrumb trail."),
        new EditorFeatureInfo(EditorFeature.Outline, "EDIT-09", "Document outline",
            "A live symbol tree (surveys, centrelines, scraps, objects) with click-to-navigate."),
        new EditorFeatureInfo(EditorFeature.PeekDefinition, "EDIT-10", "Peek definition",
            "Shows a definition inline in a popup without leaving the current file."),
        new EditorFeatureInfo(EditorFeature.SplitView, "EDIT-11", "Split / side-by-side editor",
            "Open a file in a second pane (Split Right / Split Down)."),
        new EditorFeatureInfo(EditorFeature.ColorAdornments, "EDIT-12", "Inline colour & unit hints",
            "A colour swatch next to colour values and a unit hint next to numerics."),
        new EditorFeatureInfo(EditorFeature.WhitespaceGuides, "EDIT-13", "Whitespace & indent guides",
            "Render spaces/tabs/end-of-line markers and indentation guide lines."),
        new EditorFeatureInfo(EditorFeature.TrimTrailingOnSave, "EDIT-14", "Trim whitespace on save",
            "Remove trailing whitespace and ensure a final newline when saving."),
        new EditorFeatureInfo(EditorFeature.MatchTerminator, "EDIT-15", "Match block terminator",
            "Jump between survey/endsurvey and highlight the matching pair."),
        new EditorFeatureInfo(EditorFeature.SmartEnter, "EDIT-16", "Smart Enter",
            "Auto-indent on Enter and auto-insert the matching endX for block openers."),
        new EditorFeatureInfo(EditorFeature.ReadingOrderNav, "EDIT-17", "Reading-order navigation",
            "Step into an included file and back, with a navigation backstack."),
    };

    public static EditorFeatureInfo Get(EditorFeature feature)
    {
        foreach (var info in All) if (info.Feature == feature) return info;
        throw new ArgumentOutOfRangeException(nameof(feature));
    }
}
