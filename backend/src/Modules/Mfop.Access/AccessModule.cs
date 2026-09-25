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
/// Access module: users, role assignments, devices, device keys, offline grants, trusted time, policies and offboarding.
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

        // M4: device trust, offline grants, policies, offboarding
        var grantKey = services.AddOptions<GrantSigningOptions>();
        var idp = services.AddOptions<IdentityProviderOptions>();
        if (configuration is not null)
        {
            grantKey.Bind(configuration.GetSection("Grants"));
            idp.Bind(configuration.GetSection("Keycloak"));
        }

        services.AddSingleton<GrantSigner>();
        services.AddScoped<PolicyService>();
        services.AddScoped<IPolicies>(sp => sp.GetRequiredService<PolicyService>());
        services.AddScoped<GrantService>();
        services.AddScoped<IOfflineGrants>(sp => sp.GetRequiredService<GrantService>());
        services.AddScoped<DeviceClockService>();
        services.AddScoped<IDeviceClock>(sp => sp.GetRequiredService<DeviceClockService>());
        services.AddScoped<IDeviceTrust, DeviceTrust>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<IdpTaskProcessor>();
        services.AddHttpClient<IIdentityProviderAdmin, KeycloakAdmin>();
        return services;
    }

    /// <summary>Background delivery of identity-provider tasks (API host only).</summary>
    public static IServiceCollection AddAccessWorkers(this IServiceCollection services)
    {
        services.AddHostedService<IdpTaskWorker>();
        return services;
    }
}
