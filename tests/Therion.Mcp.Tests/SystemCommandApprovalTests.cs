// A thconfig's `system` lines are shell commands Therion runs during the build. Every route an agent
// has to a build of such a thconfig must go through the host's approval, and a host with no one to
// ask must not run them unasked.

using Microsoft.Extensions.DependencyInjection;
using Therion.Core;
using Therion.Mcp.Dispatch;
using Therion.Mcp.Tools;
using Therion.Processing.Abstractions;

namespace Therion.Mcp.Tests;

public class SystemCommandApprovalTests
{
    [Fact]
    public async Task Run_command_build_does_not_build_a_thconfig_whose_system_commands_the_host_declined()
    {
        using var fixture = FixtureWorkspace.Create();
        File.AppendAllText(fixture.Thconfig, "\nsystem \"echo from-agent\"\n");
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var bridge = new RecordingBridge();
        var approval = new RecordingApproval(approve: false);

        var services = new ServiceCollection();
        services.AddSingleton<IUiBridge>(bridge);
        services.AddSingleton<IBuildApproval>(approval);
        services.AddSingleton<IWorkspaceHost>(host);
        services.AddSingleton<ITherionCompiler>(new RecordingCompiler());
        services.AddToolServices();
        using var sp = services.BuildServiceProvider();
        var tools = ActivatorUtilities.CreateInstance<CommandTools>(sp);

        await tools.RunCommand("Build");

        Assert.True(bridge.RanCommand is null,
            $"run_command reached the IDE's '{bridge.RanCommand}' although the host declined the thconfig's system commands");
    }

    [Fact]
    public async Task A_commented_input_line_still_gates_the_build()
    {
        using var fixture = FixtureWorkspace.Create();
        File.AppendAllText(fixture.Thconfig, "\ninput extra.thc # shared settings\n");
        File.WriteAllText(fixture.PathTo("extra.thc"), "system \"echo from-fragment\"\n");
        var host = new WorkspaceHost();
        await host.LoadAsync(fixture.Thconfig);
        var compiler = new RecordingCompiler();

        var result = await new BuildTools(host, compiler, new RecordingApproval(approve: false)).RunBuild();

        Assert.True(compiler.CompiledPath is null,
            "the compiler ran a thconfig whose commented input fragment holds a system line the host declined");
        Assert.Equal(ToolErrorCodes.NotTrusted, result.Error?.Code);
    }

    [Fact]
    public async Task The_headless_services_do_not_run_system_commands_unasked()
    {
        using var fixture = FixtureWorkspace.Create();
        File.AppendAllText(fixture.Thconfig, "\nsystem \"echo from-agent\"\n");
        var compiler = new RecordingCompiler();

        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceHost>(new WorkspaceHost(fixture.Thconfig));
        services.AddSingleton<ITherionCompiler>(compiler);
        services.AddToolServices();
        using var sp = services.BuildServiceProvider();

        await ActivatorUtilities.CreateInstance<BuildTools>(sp).RunBuild();

        Assert.True(compiler.CompiledPath is null,
            "the headless default compiled a thconfig with a system line without anyone approving it");
    }

    private sealed class RecordingApproval(bool approve) : IBuildApproval
    {
        public Task<bool> ApproveSystemCommandsAsync(string root, string entryPoint, IReadOnlyList<string> systemCommands,
            CancellationToken ct = default) => Task.FromResult(approve);
    }

    private sealed class RecordingCompiler : ITherionCompiler
    {
        public string? CompiledPath { get; private set; }

        public ValueTask<CompileResult> CompileAsync(
            string entryPointPath, IProgress<CompilerOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            CompiledPath = entryPointPath;
            return ValueTask.FromResult(new CompileResult(0, [], []));
        }
    }

    private sealed class RecordingBridge : IUiBridge
    {
        public string? RanCommand { get; private set; }

        public bool IsAvailable => true;
        public bool FollowAgent => true;

        public Task<UiActionResult> RunCommandAsync(string commandId)
        {
            RanCommand = commandId;
            return Task.FromResult(new UiActionResult(true, $"Ran '{commandId}'."));
        }

        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
        public Task<UiState?> GetUiStateAsync() => Task.FromResult<UiState?>(null);
        public Task<IReadOnlyList<OpenDocumentInfo>> GetOpenDocumentsAsync() =>
            Task.FromResult<IReadOnlyList<OpenDocumentInfo>>([]);
        public Task<UiActionResult> OpenFileAsync(string absolutePath, int? line) => Ok();
        public Task<UiActionResult> FocusToolAsync(string toolId) => Ok();
        public Task<UiActionResult> GotoSymbolAsync(string qualifiedName) => Ok();
        public Task<UiActionResult> ShowInThreeDAsync(string station) => Ok();
        public Task<UiActionResult> ShowToastAsync(string message, string kind) => Ok();
        private static Task<UiActionResult> Ok() => Task.FromResult(new UiActionResult(true, "ok"));
    }
}
