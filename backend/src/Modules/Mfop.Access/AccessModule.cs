using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Access;

/// <summary>
/// Access module: users and role assignments (M2: devices, offline grants).
/// Authorization data is always read from this module's tables, never from token claims (ADR-0003).
/// </summary>
public static class AccessModule
{
    public static readonly ModuleInfo Info = new("Access", "access");

    public static IServiceCollection AddAccessModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AccessDbContext>(Info.Schema);
        services.AddScoped<UserProvisioning>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        return services;
    }
}
