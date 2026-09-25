using Mfop.Evidence.Application;
using Mfop.Evidence.Contracts;
using Mfop.Evidence.Infrastructure;
using Mfop.Infrastructure;
using Mfop.Infrastructure.Storage;
using Mfop.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Mfop.Evidence;

/// <summary>
/// Evidence module: photo metadata, the upload endpoint and verification (docs/lean-pilot/06 §6.2).
/// Owns PostgreSQL schema "evidence". Other modules may only use types in the Mfop.Evidence.Contracts namespace.
/// </summary>
public static class EvidenceModule
{
    public static readonly ModuleInfo Info = new("Evidence", "evidence");

    public static IServiceCollection AddEvidenceModule(this IServiceCollection services, IConfiguration configuration, bool withBackgroundVerifier = true)
    {
        services.AddModuleDbContext<EvidenceDbContext>(Info.Schema);
        services.Configure<EvidenceOptions>(configuration.GetSection("Evidence"));
        services.Configure<ObjectStoreOptions>(configuration.GetSection("Storage"));
        services.TryAddSingleton<IObjectStore>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ObjectStoreOptions>>().Value;
            return o.Mode == "local"
                ? new LocalFileSystemObjectStore(o.Root)
                : throw new InvalidOperationException($"Storage:Mode '{o.Mode}' is not available in DEMO 1 (only 'local').");
        });
        services.AddScoped<EvidenceService>();
        services.AddScoped<IEvidenceRegistry>(sp => sp.GetRequiredService<EvidenceService>());
        services.AddScoped<EvidenceVerifier>();
        if (withBackgroundVerifier)
        {
            services.AddHostedService<EvidenceVerificationWorker>();
        }

        return services;
    }
}
