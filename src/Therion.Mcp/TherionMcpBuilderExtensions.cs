using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Therion.Mcp.Mutations;
using Therion.Mcp.Tools;

namespace Therion.Mcp;

/// <summary>
/// The single registration entry point both hosts share, so the tool catalog cannot drift between
/// the headless stdio server and the in-app HTTP server.
/// </summary>
public static class TherionMcpBuilderExtensions
{
    /// <summary>Rings R1 and R2 — everything that works without a UI.</summary>
    private static readonly Type[] HeadlessToolTypes =
    [
        typeof(ServerInfoTool),
        typeof(WorkspaceTools),
        typeof(DiagnosticsTools),
        typeof(SymbolTools),
        typeof(GraphTools),
        typeof(AggregatorTools),
        typeof(StructuralTools),
        typeof(CalculatorTools),
        typeof(RenameTools),
        typeof(FormatTools),
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
    public static IMcpServerBuilder AddTherionMcpTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        bool hasUi = builder.Services.Any(d => d.ServiceType == typeof(IUiBridge));
        builder.Services.TryAddSingleton<IUiBridge>(NullUiBridge.Instance);
        builder.Services.TryAddSingleton(_ => new WorkspaceHost());
        builder.Services.TryAddSingleton<MutationEngine>();

        // The named argument is load-bearing: a bare WithTools(someTypeArray) binds to the generic
        // WithTools<TToolType>(target) overload with TToolType = Type[], which registers nothing and
        // fails only later, as a missing tools/list capability.
        builder.WithTools(toolTypes: HeadlessToolTypes);
        if (hasUi && UiToolTypes.Length > 0) builder.WithTools(toolTypes: UiToolTypes);

        return builder;
    }
}
