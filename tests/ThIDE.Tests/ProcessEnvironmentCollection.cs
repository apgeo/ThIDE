namespace ThIDE.Tests;

/// <summary>
/// Tests that change a process-wide environment variable (PATH, APPIMAGE) run alone. While PATH holds
/// only a stub folder, a parallel test that starts a process by name (the MCP shim test runs
/// <c>dotnet</c>) would not find it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "ProcessEnvironment";
}
