// Implementation Plan §4.4 — DI hooks for command handlers (Decision D4).
// Resolves the open backlog item in §16 by exposing AddTherionCommands()
// alongside a thread-safe shared ICommandRegistry.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Therion.Syntax;

public static class TherionSyntaxHostingExtensions
{
    /// <summary>
    /// Registers <see cref="ICommandRegistry"/> as a singleton and binds every
    /// <see cref="ICommandHandler"/> registered in the container into it. Safe
    /// to call multiple times; built-in handlers can be added via
    /// <c>services.AddSingleton&lt;ICommandHandler, TMyHandler&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddTherionCommands(this IServiceCollection services)
    {
        services.TryAddSingleton<ICommandRegistry>(sp =>
        {
            var registry = new CommandRegistry();
            foreach (var handler in sp.GetServices<ICommandHandler>())
                registry.Register(handler);
            return registry;
        });
        return services;
    }

    /// <summary>Registers a single <see cref="ICommandHandler"/> implementation.</summary>
    public static IServiceCollection AddTherionCommand<THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler
    {
        services.AddSingleton<ICommandHandler, THandler>();
        return services;
    }
}
