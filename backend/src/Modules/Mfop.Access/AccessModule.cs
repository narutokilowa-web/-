using Mfop.Access.Application;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Access;

/// <summary>
/// Access module: users, role assignments (and from M2: devices, offline grants).
/// Authorization data is always read from this module's tables, never from token claims (ADR-0003).
/// </summary>
public static class AccessModule
{
    public static readonly ModuleInfo Info = new("Access", "access");

    public static IServiceCollection AddAccessModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccessDbContext>(options => options.UseMfopPostgres(connectionString, Info.Schema));
        services.AddScoped<CurrentUserService>();
        return services;
    }
}
