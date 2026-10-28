// Calling the Therion MCP tools in-process, by name, with JSON arguments — what a macro (or a test)
// needs without an MCP client and server. Arguments are bound exactly as the SDK binds them for the
// server (AIFunctionFactory, the SDK's JSON options), so a tool behaves the same either way.

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Therion.Mcp.Dispatch;

/// <param name="HasDryRun">The tool takes a <c>dryRun</c> argument (it can preview its change).</param>
/// <param name="HasExpectedSha">The tool takes <c>expectedSha256</c> (a stale-text guard).</param>
public sealed record ToolDescriptor(string Name, string Description, JsonElement Schema, bool ReadOnly, bool HasDryRun, bool HasExpectedSha);

/// <param name="Result">The tool's <c>ToolResult</c> envelope (<c>ok</c>, <c>data</c>, <c>error</c>), as JSON.</param>
public sealed record ToolCallOutcome(bool Found, bool Ok, JsonElement Result, string? Error);

public static class TherionMcpCatalog
{
    /// <summary>The tool classes a host with this profile registers (the same lists as the server).</summary>
    public static IReadOnlyList<Type> ToolTypes(McpProfile profile, bool includeUi) =>
        TherionMcpBuilderExtensions.ToolTypes(profile, includeUi);

    /// <summary>The services the tools need, with the server's defaults (a host's own registrations win).</summary>
    public static IServiceCollection AddToolServices(this IServiceCollection services)
    {
        TherionMcpBuilderExtensions.AddToolServices(services);
        return services;
    }
}

public sealed class ToolDispatcher
{
    private readonly Dictionary<string, (AIFunction Function, ToolDescriptor Descriptor)> _tools = new(StringComparer.Ordinal);

    public ToolDispatcher(IServiceProvider provider, McpProfile profile = McpProfile.Full, bool includeUi = false)
    {
        foreach (var type in TherionMcpCatalog.ToolTypes(profile, includeUi))
        {
            object? target = null;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } attr) continue;
                target ??= method.IsStatic ? null : ActivatorUtilities.GetServiceOrCreateInstance(provider, type);
                var name = attr.Name ?? method.Name;
                var fn = AIFunctionFactory.Create(method, method.IsStatic ? null : target,
                    new AIFunctionFactoryOptions { SerializerOptions = McpJsonUtilities.DefaultOptions, Name = name });
                var parameters = method.GetParameters();
                var descriptor = new ToolDescriptor(name, fn.Description, fn.JsonSchema, attr.ReadOnly,
                    parameters.Any(p => p.Name == "dryRun"), parameters.Any(p => p.Name == "expectedSha256"));
                _tools[name] = (fn, descriptor);
            }
        }
    }

    public IReadOnlyList<ToolDescriptor> Tools => _tools.Values.Select(t => t.Descriptor).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    public ToolDescriptor? Find(string name) => _tools.TryGetValue(name, out var t) ? t.Descriptor : null;

    /// <summary>Calls a tool. Never throws for the tool's own failures: they come back in the outcome.</summary>
    public async Task<ToolCallOutcome> CallAsync(string name, JsonElement args, CancellationToken ct = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return new ToolCallOutcome(false, false, default, $"no tool named '{name}'");
        var arguments = new AIFunctionArguments();
        if (args.ValueKind == JsonValueKind.Object)
            foreach (var p in args.EnumerateObject()) arguments[p.Name] = p.Value;
        object? result;
        try { result = await tool.Function.InvokeAsync(arguments, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ToolCallOutcome(true, false, default, ex.InnerException?.Message ?? ex.Message);
        }
        var json = result is JsonElement e ? e : JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
        bool ok = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True;
        string? error = !ok && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object
            ? (err.TryGetProperty("message", out var m) ? m.GetString() : err.ToString())
            : null;
        return new ToolCallOutcome(true, ok, json, error);
    }
}
