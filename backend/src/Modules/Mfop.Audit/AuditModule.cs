using Mfop.Audit.Application;
using Mfop.Audit.Contracts;
using Mfop.Audit.Infrastructure;
using Mfop.Infrastructure.Auditing;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Audit;

/// <summary>
/// Audit module: the hash-chained, append-only audit log (ADR-0007).
/// Owns PostgreSQL schema "audit". Other modules use it only through Mfop.Audit.Contracts and IAuditTrail (Kernel).
/// </summary>
public static class AuditModule
{
    public static readonly ModuleInfo Info = new("Audit", "audit");

    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        // Read-only context (queries); NOT a unit-of-work participant. Writes go through the appender.
        services.AddDbContext<AuditDbContext>((sp, options) => options.UseMfopPostgres(sp.GetRequiredService<DbSession>().Connection, Info.Schema));
        services.AddScoped<IAuditAppender, AuditAppender>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        return services;
    }
}
