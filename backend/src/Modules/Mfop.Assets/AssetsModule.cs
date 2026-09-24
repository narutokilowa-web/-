using Mfop.Assets.Application;
using Mfop.Assets.Domain;
using Mfop.Assets.Infrastructure;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Assets;

/// <summary>
/// Assets module: street lights, registrations (maker/checker), approved versions, QR tags.
/// Owns PostgreSQL schema "assets". Other modules may only use types in the Mfop.Assets.Contracts namespace.
/// </summary>
public static class AssetsModule
{
    public static readonly ModuleInfo Info = new("Assets", "assets");

    public static IServiceCollection AddAssetsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AssetsDbContext>(Info.Schema);
        services.Configure<TagOptions>(configuration.GetSection("Tags"));
        services.AddScoped<RegistrationCommands>();
        services.AddScoped<AssetQueries>();
        return services;
    }
}
