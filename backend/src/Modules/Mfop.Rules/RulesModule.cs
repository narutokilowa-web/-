using Mfop.Infrastructure;
using Mfop.Kernel;
using Mfop.Rules.Application;
using Mfop.Rules.Contracts;
using Mfop.Rules.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Rules;

/// <summary>
/// Rules module: rule settings (data, changed only by ADMIN, audited), the minimal repeated-failure rule and its
/// recommendations (DEMO 1), and integrity flags raised by other modules (the auditor flag workflow is M6).
/// Owns PostgreSQL schema "rules". Other modules may only use types in the Mfop.Rules.Contracts namespace.
/// </summary>
public static class RulesModule
{
    public static readonly ModuleInfo Info = new("Rules", "rules");

    public static IServiceCollection AddRulesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<RulesDbContext>(Info.Schema);
        services.AddScoped<RuleSettingsService>();
        services.AddScoped<RepeatedFailureRule>();
        services.AddScoped<IRepeatedFailureRule>(sp => sp.GetRequiredService<RepeatedFailureRule>());
        services.AddScoped<IRecommendations>(sp => sp.GetRequiredService<RepeatedFailureRule>());
        services.AddScoped<IFlags, FlagService>();
        return services;
    }
}
