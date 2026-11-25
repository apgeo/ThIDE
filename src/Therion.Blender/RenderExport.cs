// An export: the scene written out for someone to run in Blender themselves, instead of ThIDE
// running it. What is written is the same scene a render shows, by the same builder: the player's
// folder (a scene plan, the player, the meshes and the images, with scripts that start Blender,
// as a render bundle holds) or one script that carries all of it; or, when the script generator
// is asked for, its script. An export "to look at" is the scene for Blender's window: it is
// started with the arguments the export names, which make the player build the scene and stop.

namespace Therion.Blender;

/// <summary>How a scene is exported.</summary>
public sealed record ExportOptions
{
    /// <summary>What builds the scene when the export is run. <see cref="SceneBuilder.Auto"/>
    /// is the player, as for a render.</summary>
    public SceneBuilder Builder { get; init; } = SceneBuilder.Auto;

    /// <summary>One file instead of a folder. The player's is a script that carries the plan,
    /// the player and every file the plan names (meshes, images, photos, the world's image);
    /// the script generator's is its self-contained script with the meshes embedded.</summary>
    public bool SingleFile { get; init; }

    /// <summary>The export is for looking at the scene in Blender's window, not for rendering
    /// it: a job folder as a render has it, to be started without <c>-b</c> and with
    /// <see cref="RenderExport.ScriptArguments"/>. Its output goes where the spec says, or to
    /// <c>out</c> in the folder. <see cref="SingleFile"/> does not go with it.</summary>
    public bool Interactive { get; init; }

    /// <summary>The language of the README of a player's folder: "en" or "ro"; any other gives
    /// the English one.</summary>
    public string Language { get; init; } = "en";
}

/// <summary>What an export wrote.</summary>
public sealed record RenderExport
{
    /// <summary>The script to start Blender with.</summary>
    public required string ScriptPath { get; init; }

    /// <summary>The folder it was written to.</summary>
    public required string Directory { get; init; }

    /// <summary>What builds the scene when the script runs.</summary>
    public SceneBuilder Builder { get; init; } = SceneBuilder.Player;

    /// <summary>True when the export is one file.</summary>
    public bool SingleFile { get; init; }

    /// <summary>What the script is given after <c>--</c> on Blender's command line when it is
    /// started for what it was exported for: <c>--build-only</c> for an export to look at,
    /// nothing for one to render.</summary>
    public IReadOnlyList<string> ScriptArguments { get; init; } = [];

    /// <summary>The files written, relative to <see cref="Directory"/>, with forward slashes.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>How many frames the scene renders, the frames of an end card included.</summary>
    public int FrameCount { get; init; }

    /// <summary>True when the scene has texts the player writes over the finished frames, in
    /// its second stage: they are in the render, and not in the scene Blender's window shows.</summary>
    public bool TextsInSecondStage { get; init; }

    /// <summary>The settings of the spec the exported script does not show, as field paths:
    /// what the script generator does not draw. Empty for the player.</summary>
    public IReadOnlyList<string> LeftOut { get; init; } = [];

    /// <summary>What the scene shows less of than the spec asks for
    /// (<see cref="Plan.ScenePlanBuilder.Notices"/>); empty for the script generator, whose
    /// script prints the same when it runs.</summary>
    public IReadOnlyList<string> Notices { get; init; } = [];
}
