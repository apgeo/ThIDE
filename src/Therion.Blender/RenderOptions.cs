// How a render is to be made, beside what it shows (SceneSpec): which of the two scene builders
// builds the scene in Blender, whether it is a quick draft, and what becomes of the frames of a
// render in two stages. Plain data, and one pure function that says which builder a spec gets.
//
// There are two builders. The player builds a scene from a scene plan; it draws the texts over
// the picture in a second stage, renders drafts and can go on with a render that was stopped. The
// script generator writes a Python script for the one scene; it is kept as it was, for what the
// player does not carry yet and for comparison.

using Therion.Blender.Plan;

namespace Therion.Blender;

/// <summary>What builds the scene in Blender.</summary>
public enum SceneBuilder
{
    /// <summary>The player when it carries everything the spec asks for, otherwise the script
    /// generator.</summary>
    Auto,
    /// <summary>The player; a spec it cannot carry is refused, with what it cannot carry.</summary>
    Player,
    /// <summary>The script generator.</summary>
    Generator,
}

/// <summary>How a render is to be made.</summary>
public sealed record RenderOptions
{
    public SceneBuilder Builder { get; init; } = SceneBuilder.Auto;

    /// <summary>A quick draft instead of the render: half the size, in Blender's solid view,
    /// written beside the real output under the base name with <c>-draft</c> added. Always built
    /// by the player, whatever <see cref="Builder"/> says.</summary>
    public bool Draft { get; init; }

    /// <summary>Keep the frames a movie is made from. The render then takes two stages (the
    /// frames, then the output from them) whether it has texts or not.</summary>
    public bool KeepFrames { get; init; }

    /// <summary>Render nothing: write the output again from the frames in
    /// <see cref="FramesDirectory"/>, with the texts as the spec has them now. For a title or a
    /// caption that changed; the scene must be the one the frames were rendered from.</summary>
    public bool ComposeOnly { get; init; }

    /// <summary>Where the frames of a render in two stages are kept. A render that finds frames
    /// of the same scene there goes on with them, so a render that was stopped can be started
    /// again. Null keeps them in the render's scratch folder (or, with <see cref="KeepFrames"/>,
    /// in <c>&lt;base name&gt;-frames</c> beside the output), where nothing is continued.</summary>
    public string? FramesDirectory { get; init; }
}

/// <summary>Which builder a spec gets, and why.</summary>
/// <param name="Builder">The builder that builds the scene; meaningless when
/// <paramref name="Refusal"/> is set.</param>
/// <param name="NotCarried">The settings of the spec the player does not carry
/// (<see cref="ScenePlanBuilder.NotCarried"/>), when they decided: they made
/// <see cref="SceneBuilder.Auto"/> take the script generator, or the player refuse.</param>
/// <param name="LeftOut">The settings of the spec the chosen builder does not show: for the
/// script generator the texts only the player draws, for a draft what the player does not
/// carry.</param>
/// <param name="Refusal">Why the render cannot be made as asked; null when it can.</param>
public sealed record SceneBuilderChoice(
    SceneBuilder Builder, IReadOnlyList<string> NotCarried, IReadOnlyList<string> LeftOut, string? Refusal = null);

/// <summary>Chooses between the two scene builders.</summary>
public static class SceneBuilders
{
    /// <summary>
    /// The builder for <paramref name="spec"/> under <paramref name="options"/>. A draft is
    /// always the player's. Otherwise <see cref="SceneBuilder.Auto"/> takes the player when it
    /// carries the whole spec and the script generator when it does not;
    /// <see cref="SceneBuilder.Player"/> refuses a spec the player cannot carry; and keeping the
    /// frames or composing from them needs the player.
    /// </summary>
    public static SceneBuilderChoice Choose(SceneSpec spec, RenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        options ??= new RenderOptions();
        var lost = ScenePlanBuilder.NotCarried(spec);
        if (options.Draft)
            return new SceneBuilderChoice(SceneBuilder.Player, [], lost);

        bool staged = options.KeepFrames || options.ComposeOnly;
        string stagedWhat = options.ComposeOnly ? "Writing the output from the frames that are there" : "Keeping the frames";
        switch (options.Builder)
        {
            case SceneBuilder.Generator:
                return staged
                    ? new SceneBuilderChoice(SceneBuilder.Generator, [], [],
                        $"{stagedWhat} needs the player: the script generator renders in one stage.")
                    : new SceneBuilderChoice(SceneBuilder.Generator, [], NotDrawnByTheGenerator(spec));

            case SceneBuilder.Player:
                return lost.Count == 0
                    ? new SceneBuilderChoice(SceneBuilder.Player, [], [])
                    : new SceneBuilderChoice(SceneBuilder.Player, lost, [],
                        $"The player cannot build this scene: it does not carry {string.Join(", ", lost)}. " +
                        "Choose the script generator, or let the builder be chosen automatically.");

            case SceneBuilder.Auto:
                if (lost.Count == 0) return new SceneBuilderChoice(SceneBuilder.Player, [], []);
                return staged
                    ? new SceneBuilderChoice(SceneBuilder.Generator, lost, [],
                        $"{stagedWhat} needs the player, which does not carry {string.Join(", ", lost)}.")
                    : new SceneBuilderChoice(SceneBuilder.Generator, lost, NotDrawnByTheGenerator(spec));

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Builder, "Unknown scene builder.");
        }
    }

    /// <summary>The overlays of <paramref name="spec"/> a generated script does not draw, as
    /// field paths: everything that was added after the script generator was last changed, which
    /// only the player's second stage draws.</summary>
    public static IReadOnlyList<string> NotDrawnByTheGenerator(SceneSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var overlays = spec.Labels.Overlays;
        var lost = new List<string>();
        bool titled = !string.IsNullOrWhiteSpace(overlays.Title);
        if (titled && !string.IsNullOrWhiteSpace(overlays.Subtitle)) lost.Add("labels.overlays.subtitle");
        if (titled && overlays.TitleSeconds is not null) lost.Add("labels.overlays.titleSeconds");
        if (!string.IsNullOrWhiteSpace(overlays.Credits)) lost.Add("labels.overlays.credits");
        if (!string.IsNullOrWhiteSpace(overlays.Corner)) lost.Add("labels.overlays.corner");
        if (overlays.CornerCaptions.Count > 0) lost.Add("labels.overlays.cornerCaptions");
        if (overlays.Captions.Count > 0) lost.Add("labels.overlays.captions");
        if (overlays.EndCard is not null) lost.Add("labels.overlays.endCard");
        if (overlays.FadeFromBlack > 0) lost.Add("labels.overlays.fadeFromBlack");
        if (overlays.FadeToBlack > 0) lost.Add("labels.overlays.fadeToBlack");
        return lost;
    }
}
