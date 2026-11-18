// The Blender Animation panel's renderer, model sources and saved presets, lent to the in-app MCP
// server so an agent renders exactly as the panel does: the same Blender (and its path override), the
// same workspace model with its open leads, the same re-export, the same job folders.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Therion.Blender;
using Therion.Blender.Presets;
using Therion.Blender.Sources;
using Therion.Mcp;

namespace ThIDE.Services;

public sealed class McpBlenderRenderHost(
    IBlenderRenderService renderer, IBlenderSourceProvider sources, PresetStore presets) : IBlenderRenderHost
{
    public IBlenderRenderService Renderer => renderer;

    public IReadOnlyList<RenderPreset> UserPresets()
    {
        // The store skips files it cannot parse; a folder it cannot list leaves the built-in presets.
        try { return presets.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public Task<RenderSource> AcquireAsync(ModelSourceRequest request, CancellationToken ct = default) =>
        sources.AcquireAsync(request, ct);
}
