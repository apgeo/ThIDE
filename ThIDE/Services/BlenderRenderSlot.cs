// Renders run one at a time, whoever starts them: the Blender Animation panel and agents through the
// in-app MCP server share this slot. Two Blender renders at once would only slow each other down and
// fight over the GPU. An agent's finished render is announced so the panel can list it in its history.

using System;
using System.Collections.Generic;
using Therion.Blender;

namespace ThIDE.Services;

/// <summary>An agent's render that has ended: how, and with what outputs.</summary>
public sealed record AgentRenderFinished(string Name, RenderResult? Result, string? Error);

public sealed class BlenderRenderSlot
{
    private readonly object _lock = new();
    private string? _holder;

    /// <summary>The slot was taken or given back. Raised on the thread that did it.</summary>
    public event EventHandler? Changed;

    /// <summary>An agent's render ended. Raised on the thread that ran it.</summary>
    public event EventHandler<AgentRenderFinished>? AgentRenderEnded;

    /// <summary>Who holds the slot ("panel", or an agent's render name); null when it is free.</summary>
    public string? Holder { get { lock (_lock) return _holder; } }

    public bool IsTaken => Holder is not null;

    /// <summary>Takes the slot for <paramref name="holder"/>; false when someone holds it.</summary>
    public bool TryTake(string holder)
    {
        ArgumentException.ThrowIfNullOrEmpty(holder);
        lock (_lock)
        {
            if (_holder is not null) return false;
            _holder = holder;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Gives the slot back.</summary>
    public void Release()
    {
        lock (_lock) _holder = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gives back an agent's slot and announces how its render ended.</summary>
    public void ReleaseAgent(AgentRenderFinished finished)
    {
        Release();
        AgentRenderEnded?.Invoke(this, finished);
    }
}
