using Mfop.Access.Api;
using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Access;

/// <summary>
/// Access module: users, role assignments and devices (offline grants: M4).
/// Authorization data is always read from this module's tables, never from token claims (ADR-0003).
/// </summary>
public static class AccessModule
{
    public static readonly ModuleInfo Info = new("Access", "access");

    /// <param name="configuration">Host configuration; section "Auth" provides the field-app client id. Null in the migrator.</param>
    public static IServiceCollection AddAccessModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var deviceGuard = services.AddOptions<DeviceGuardOptions>();
        if (configuration is not null)
        {
            deviceGuard.Bind(configuration.GetSection("Auth"));
        }

        services.AddModuleDbContext<AccessDbContext>(Info.Schema);
        services.AddScoped<UserProvisioning>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<DeviceService>();
        services.AddScoped<IAssigneeDirectory, AssigneeDirectory>();
        return services;
    }
}
