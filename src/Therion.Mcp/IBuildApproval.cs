// A thconfig can carry `system` lines, which Therion runs as shell commands during the build. When a
// model asks for the build, those commands may be ones it wrote itself a moment earlier, so run_build
// asks the host before it starts one. The in-app host asks the user whether the folder is trusted —
// the same question hooks and tasks ask. A headless server has no one to ask: the person who pointed
// it at a folder is the one who chose to run that folder's Therion, so it allows the build as before.

namespace Therion.Mcp;

public interface IBuildApproval
{
    /// <summary>
    /// True when the build of <paramref name="entryPoint"/> under <paramref name="root"/> may run the
    /// thconfig's <paramref name="systemCommands"/> (never empty when this is called).
    /// </summary>
    Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
        CancellationToken ct = default);
}

/// <summary>The headless default: no one to ask, so every build runs as Therion would run it.</summary>
public sealed class AllowAllBuildApproval : IBuildApproval
{
    public static readonly AllowAllBuildApproval Instance = new();

    private AllowAllBuildApproval() { }

    public Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
        CancellationToken ct = default) => Task.FromResult(true);
}
