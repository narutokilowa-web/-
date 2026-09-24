using Mfop.Infrastructure.Auditing;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Mfop.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Core persistence services: data source, per-scope session, unit of work, audit trail, current actor.</summary>
    public static IServiceCollection AddMfopPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddScoped<DbSession>();
        services.AddScoped<AuditTrail>();
        services.AddScoped<IAuditTrail>(sp => sp.GetRequiredService<AuditTrail>());
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<CurrentActor>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>Registers a module DbContext on the shared session connection and enlists it in the unit of work.</summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((sp, options) => options.UseMfopPostgres(sp.GetRequiredService<DbSession>().Connection, schema));
        services.AddScoped<IUnitOfWorkParticipant, UnitOfWorkParticipant<TContext>>();
        return services;
    }
}
