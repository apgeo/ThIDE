// Rendering a cave in Blender starts a long-running external program. This library knows only the pure
// Blender types (scene descriptions, presets, the render-service seam); finding and running Blender, and
// finding the workspace's model, belong to the host. A host that can render registers one of these, and
// its presence is what makes AddTherionMcpTools register the render tools, as an IUiBridge does for the
// UI tools. The headless server registers none.

using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Sources;

namespace Therion.Mcp;

public interface IBlenderRenderHost
{
    /// <summary>Converts the model, writes the Blender script, and runs Blender.</summary>
    IBlenderRenderService Renderer { get; }

    /// <summary>The user's saved presets, besides the built-in ones. Empty when the host keeps none.</summary>
    IReadOnlyList<RenderPreset> UserPresets();

    /// <summary>
    /// The model a render reads and the workspace leads to mark in it: the file
    /// <paramref name="request"/> names, or else the workspace's newest model (re-exported with Therion
    /// when the request allows it and the model is missing or older than the sources).
    /// </summary>
    /// <exception cref="ModelSourceNotFoundException">There is no model to render.</exception>
    Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default);

    /// <summary>
    /// Claims the host's render slot for an agent's render named <paramref name="name"/>; false when a
    /// render the host started itself (its own panel) holds it. Renders run one at a time.
    /// </summary>
    bool TryBeginRender(string name) => true;

    /// <summary>Gives the slot back once the render has ended: its result, or the error that stopped it
    /// before Blender gave one.</summary>
    void EndRender(string name, RenderResult? result, string? error) { }
}
