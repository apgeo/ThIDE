namespace Therion.Build.Automation;

/// <summary>How one Therion build ended — what hooks, tasks and the batch build react to.</summary>
/// <param name="Entry">The thconfig (or file) that was compiled.</param>
/// <param name="Label">A caller's name for the run ("Batch 2/5", "cave.thconfig :: plan PDF").</param>
public sealed record BuildOutcome(
    string Entry,
    bool Succeeded,
    bool Cancelled,
    int ExitCode,
    int Errors,
    int Warnings,
    IReadOnlyList<string> Artifacts,
    string? LogPath,
    TimeSpan Elapsed,
    string? Label = null);
