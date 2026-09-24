using Mfop.Access.Contracts;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Mfop.Sync.Application;
using Mfop.Sync.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Sync;

/// <summary>
/// Sync module: the phone's single write path (push), its read path (pull) and online tag resolution.
/// Owns PostgreSQL schema "sync". Other modules may only use types in the Mfop.Sync.Contracts namespace.
/// </summary>
public static class SyncModule
{
    public static readonly ModuleInfo Info = new("Sync", "sync");

    public static IServiceCollection AddSyncModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<SyncDbContext>(Info.Schema);
        services.AddScoped<SyncService>();
        services.AddScoped<IDeviceSyncInfo, DeviceSyncInfo>();
        return services;
    }
}
