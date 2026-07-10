using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp;

/// <summary>How much of the catalog a host is allowed to see.</summary>
public enum McpProfile
{
    /// <summary>Read-only. Nothing in this profile can change a file, a sidecar, or run a program.</summary>
    Data,

    /// <summary>Everything: reads, mutations, exports, and the compiler. The default.</summary>
    Full,
}

/// <summary>
/// The single registration entry point both hosts share, so the tool catalog cannot drift between
/// the headless stdio server and the in-app HTTP server.
/// </summary>
public static class TherionMcpBuilderExtensions
{
    /// <summary>
    /// Ring R1. Every tool here is annotated <c>readOnlyHint</c>, and the <c>data</c> profile is
    /// exactly this list — so the profile boundary and the annotations cannot disagree.
    /// </summary>
    private static readonly Type[] ReadOnlyToolTypes =
    [
        typeof(ServerInfoTool),
        typeof(WorkspaceTools),
        typeof(DiagnosticsTools),
        typeof(SymbolTools),
        typeof(GraphTools),
        typeof(AggregatorTools),
        typeof(StructuralTools),
        typeof(CalculatorTools),
        typeof(ProjectMetadataTools),
    ];

    /// <summary>Ring R2 — writes files, writes sidecars, or runs the compiler.</summary>
    private static readonly Type[] MutatingToolTypes =
    [
        typeof(RenameTools),
        typeof(FormatTools),
        typeof(ScaffoldTools),
        typeof(ImportTools),
        typeof(ExportTools),
        typeof(ProjectStateTools),
        typeof(BuildTools),
    ];

    /// <summary>Ring R3 — registered only when the caller supplied a real <see cref="IUiBridge"/>.</summary>
    private static readonly Type[] UiToolTypes = [];

    /// <summary>
    /// Registers the Therion tool catalog on an MCP server builder. Call <em>after</em> registering
    /// an <see cref="IUiBridge"/> if the host has a UI: the presence of one at this point is what
    /// selects the R3 catalog. Hosts without a UI get <see cref="NullUiBridge"/> and no R3 tools.
    /// A host that wants the server to open a workspace at startup registers its own
    /// <see cref="WorkspaceHost"/> beforehand; otherwise an empty one is created and the model must
    /// call <c>load_workspace</c>.
    /// </summary>
    /// <param name="profile">
    /// <see cref="McpProfile.Data"/> registers only the read-only tools. A model cannot be talked into
    /// using a tool that was never registered, which is a stronger guarantee than a confirmation prompt.
    /// </param>
    public static IMcpServerBuilder AddTherionMcpTools(
        this IMcpServerBuilder builder, McpProfile profile = McpProfile.Full)
    {
        ArgumentNullException.ThrowIfNull(builder);

        bool hasUi = builder.Services.Any(d => d.ServiceType == typeof(IUiBridge));
        builder.Services.TryAddSingleton<IUiBridge>(NullUiBridge.Instance);
        builder.Services.TryAddSingleton(_ => new WorkspaceHost());
        builder.Services.TryAddSingleton<MutationEngine>();

        // The same per-root sidecars the IDE reads: a lead the model marks pushed is one the caver
        // sees marked pushed (D-027). A host with its own instances registers them first.
        builder.Services.TryAddSingleton<Workspace.IProjectMetadataStore>(_ => new Workspace.ProjectMetadataStore());
        builder.Services.TryAddSingleton<Workspace.ILeadStatusStore>(_ => new Workspace.LeadStatusStore());

        // The real compiler, found on PATH or in the usual places. A test — or the in-app host, which
        // has the user's configured override — registers its own first.
        builder.Services.TryAddSingleton<Processing.Abstractions.IExternalToolLocator>(_ => new Build.ExternalToolLocator());
        builder.Services.TryAddSingleton<Processing.Abstractions.ITherionCompiler>(sp =>
            new Build.TherionCompiler(sp.GetRequiredService<Processing.Abstractions.IExternalToolLocator>()));

        // The named argument is load-bearing: a bare WithTools(someTypeArray) binds to the generic
        // WithTools<TToolType>(target) overload with TToolType = Type[], which registers nothing and
        // fails only later, as a missing tools/list capability.
        builder.WithTools(toolTypes: ReadOnlyToolTypes);

        if (profile is McpProfile.Full)
        {
            builder.WithTools(toolTypes: MutatingToolTypes);
            if (hasUi && UiToolTypes.Length > 0) builder.WithTools(toolTypes: UiToolTypes);
        }

        return builder;
    }
}
