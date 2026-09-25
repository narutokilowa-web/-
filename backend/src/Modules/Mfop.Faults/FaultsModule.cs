using Mfop.Assets.Contracts;
using Mfop.Faults.Application;
using Mfop.Faults.Contracts;
using Mfop.Faults.Infrastructure;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Faults;

/// <summary>
/// Faults module: fault reports and duplicate confirmations on street lights.
/// Owns PostgreSQL schema "faults". Other modules may only use types in the Mfop.Faults.Contracts namespace.
/// </summary>
public static class FaultsModule
{
    public static readonly ModuleInfo Info = new("Faults", "faults");

    public static IServiceCollection AddFaultsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<FaultsDbContext>(Info.Schema);
        services.AddScoped<FaultService>();
        services.AddScoped<FaultQueries>();
        services.AddScoped<IFaultWorkflow, FaultWorkflow>();
        services.AddScoped<IAssetRelatedHistory, FaultAssetHistory>();
        services.AddScoped<ISyncCommandHandler, ReportFaultSyncHandler>();
        return services;
    }
}
