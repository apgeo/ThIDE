// Implementation Plan �5.3 � DI hooks for semantic rule plugins (Decision D3).
// Exposes AddTherionSemantics() so the composition root can wire the rule
// runner alongside any registered ISemanticRule implementations.

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Therion.Semantics.BuiltinRules;
using Therion.Semantics.UserRules;

namespace Therion.Semantics;

public static class TherionSemanticsHostingExtensions
{
    /// <summary>
    /// Registers <see cref="ISemanticRuleRunner"/> and <see cref="IModelEditService"/>.
    /// Rules added via <c>services.AddSingleton&lt;ISemanticRule, TMyRule&gt;()</c>
    /// (or <see cref="AddTherionSemanticRule{TRule}"/>) are picked up automatically.
    /// </summary>
    public static IServiceCollection AddTherionSemantics(this IServiceCollection services)
    {
        services.TryAddSingleton<ISemanticRuleRunner>(sp =>
            new SemanticRuleRunner(sp.GetServices<ISemanticRule>()));
        services.TryAddSingleton<IModelEditService, ModelEditService>();
        return services;
    }

    /// <summary>
    /// Registers the rule runner honouring a user <see cref="SemanticRuleConfig"/>: the
    /// config's disabled-rule ids are filtered out of the runner, and any naming-convention specs
    /// are registered as a <see cref="UserRules.NamingConventionRule"/>.
    /// </summary>
    public static IServiceCollection AddTherionSemantics(
        this IServiceCollection services, SemanticRuleConfig config)
    {
        var options = config.ToRunnerOptions();
        var namingSpecs = config.ToNamingSpecs();
        if (!namingSpecs.IsDefaultOrEmpty)
            services.AddSingleton<ISemanticRule>(new UserRules.NamingConventionRule(namingSpecs));

        services.TryAddSingleton<ISemanticRuleRunner>(sp =>
            new SemanticRuleRunner(sp.GetServices<ISemanticRule>(), options));
        services.TryAddSingleton<IModelEditService, ModelEditService>();
        return services;
    }

    public static IServiceCollection AddTherionSemanticRule<TRule>(this IServiceCollection services)
        where TRule : class, ISemanticRule
    {
        services.AddSingleton<ISemanticRule, TRule>();
        return services;
    }

    /// <summary>
    /// Registers the built-in stock semantic rules shipped with <c>Therion.Semantics</c>
    /// (Plan �5.3 / M6 follow-up #6). Call once from the composition root; pair with
    /// <see cref="AddTherionSemantics"/> so the runner picks them up.
    /// </summary>
    public static IServiceCollection AddTherionBuiltinSemanticRules(this IServiceCollection services)
    {
        services.AddTherionSemanticRule<OrphanFixedStationRule>();
        return services;
    }
}

