// A thconfig can carry `system` lines, which Therion runs as shell commands during the build. When a
// model asks for the build, those commands may be ones it wrote itself a moment earlier, so every
// agent-started build asks the host before it starts one. The in-app host asks the user whether the
// folder is trusted — the same question hooks and tasks ask. A headless server has no one to ask, so it
// refuses such a build unless whoever started it said up front that it may run them
// (--allow-system-commands).

namespace Therion.Mcp;

public interface IBuildApproval
{
    /// <summary>
    /// True when the build of <paramref name="entryPoint"/> under <paramref name="root"/> may run the
    /// thconfig's <paramref name="systemCommands"/> (never empty when this is called).
    /// </summary>
    Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
        CancellationToken ct = default);

    /// <summary>Why a declined build did not run and what would let it run, in terms a model can relay.</summary>
    string DeclinedReason =>
        "The user has not trusted this folder to run commands. Ask the user to build it themselves, or to trust the folder.";
}

/// <summary>
/// The headless default: no one to ask, so a build that would run system commands is refused. The
/// refusal says how to opt in.
/// </summary>
public sealed class RefuseSystemCommandsBuildApproval : IBuildApproval
{
    public static readonly RefuseSystemCommandsBuildApproval Instance = new();

    private RefuseSystemCommandsBuildApproval() { }

    public Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
        CancellationToken ct = default) => Task.FromResult(false);

    public string DeclinedReason =>
        "This server does not run a thconfig's system commands unless it was started with "
        + AllowAllBuildApproval.CommandLineFlag + ". Ask the user to build it themselves (in ThIDE or with "
        + "Therion), or, if they trust this folder, to restart the server with " + AllowAllBuildApproval.CommandLineFlag + ".";
}

/// <summary>
/// The headless server started with <c>--allow-system-commands</c>: the person who started it chose to
/// let builds run the folder's commands, so every build runs as Therion would run it.
/// </summary>
public sealed class AllowAllBuildApproval : IBuildApproval
{
    /// <summary>The headless command-line option that registers this approval.</summary>
    public const string CommandLineFlag = "--allow-system-commands";

    public static readonly AllowAllBuildApproval Instance = new();

    private AllowAllBuildApproval() { }

    public Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
        CancellationToken ct = default) => Task.FromResult(true);
}
