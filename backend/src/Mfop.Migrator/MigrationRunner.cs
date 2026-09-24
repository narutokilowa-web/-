using System.Text.Json;
using Mfop.Access;
using Mfop.Access.Application;
using Mfop.Access.Infrastructure;
using Mfop.Assets;
using Mfop.Assets.Infrastructure;
using Mfop.Audit;
using Mfop.Audit.Infrastructure;
using Mfop.Faults;
using Mfop.Faults.Infrastructure;
using Mfop.Sync;
using Mfop.Sync.Infrastructure;
using Mfop.WorkOrders;
using Mfop.WorkOrders.Infrastructure;
using Mfop.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Migrator;

/// <summary>
/// Applies module migrations as the owner role and then grants runtime privileges:
/// mfop_app gets SELECT/INSERT/UPDATE (never DELETE); append-only tables get SELECT/INSERT only.
/// </summary>
public sealed class MigrationRunner(string ownerConnectionString, TextWriter log)
{
    private static readonly string[] ModuleSchemas =
    [
        AccessModule.Info.Schema, AuditModule.Info.Schema, AssetsModule.Info.Schema, FaultsModule.Info.Schema,
        WorkOrdersModule.Info.Schema, SyncModule.Info.Schema,
    ];

    /// <summary>Tables whose rows are never updated. mfop_app gets no UPDATE and a trigger rejects UPDATE/DELETE.</summary>
    public static readonly string[] AppendOnlyTables =
    [
        "audit.audit_events", "assets.asset_versions", "assets.asset_status_events", "faults.fault_confirmations", "sync.sync_commands",
    ];

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using (var access = new AccessDbContext(Options<AccessDbContext>(AccessModule.Info.Schema)))
        {
            await access.Database.MigrateAsync(ct);
        }

        await using (var audit = new AuditDbContext(Options<AuditDbContext>(AuditModule.Info.Schema)))
        {
            await audit.Database.MigrateAsync(ct);
        }

        await using (var assets = new AssetsDbContext(Options<AssetsDbContext>(AssetsModule.Info.Schema)))
        {
            await assets.Database.MigrateAsync(ct);
        }

        await using (var faults = new FaultsDbContext(Options<FaultsDbContext>(FaultsModule.Info.Schema)))
        {
            await faults.Database.MigrateAsync(ct);
        }

        await using (var workOrders = new WorkOrdersDbContext(Options<WorkOrdersDbContext>(WorkOrdersModule.Info.Schema)))
        {
            await workOrders.Database.MigrateAsync(ct);
        }

        await using (var sync = new SyncDbContext(Options<SyncDbContext>(SyncModule.Info.Schema)))
        {
            await sync.Database.MigrateAsync(ct);
        }

        log.WriteLine("migrations applied: access, audit, assets, faults, workorders, sync");
        await ApplyGrantsAsync(ct);
    }

    private async Task ApplyGrantsAsync(CancellationToken ct)
    {
        await using var db = new AccessDbContext(Options<AccessDbContext>(AccessModule.Info.Schema));
        foreach (var schema in ModuleSchemas)
        {
            // Schema and table names come from code constants, not user input.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"""
                GRANT USAGE ON SCHEMA {schema} TO mfop_app, mfop_readonly;
                GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA {schema} TO mfop_app;
                REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA {schema} FROM mfop_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO mfop_app;
                GRANT SELECT ON ALL TABLES IN SCHEMA {schema} TO mfop_readonly;
                REVOKE ALL ON {schema}.__ef_migrations_history FROM mfop_app;
                """, ct);
        }

        foreach (var table in AppendOnlyTables)
        {
            await db.Database.ExecuteSqlRawAsync($"REVOKE UPDATE ON {table} FROM mfop_app;", ct);
        }
#pragma warning restore EF1002
        log.WriteLine("grants applied (mfop_app: SELECT/INSERT/UPDATE, no DELETE; append-only tables: SELECT/INSERT)");
    }

    /// <summary>
    /// Dev bootstrap: ensures the dev users (fixed ids matching the Keycloak dev realm) and their roles exist.
    /// Runs through the same unit of work as the application, so every created user and granted role is an
    /// audited genesis event (actor SYSTEM, reason BOOTSTRAP_DEV_SEED). Idempotent.
    /// </summary>
    public async Task SeedDevUsersAsync(string path, CancellationToken ct)
    {
        var file = JsonSerializer.Deserialize<DevUsersFile>(await File.ReadAllTextAsync(path, ct), JsonOptions)
                   ?? throw new InvalidOperationException($"Cannot read {path}");

        var services = new ServiceCollection();
        services.AddMfopPersistence(ownerConnectionString);
        services.AddAuditModule();
        services.AddAccessModule();
        await using var provider = services.BuildServiceProvider();

        foreach (var u in file.Users)
        {
            await using var scope = provider.CreateAsyncScope();
            var provisioning = scope.ServiceProvider.GetRequiredService<UserProvisioning>();
            var result = await provisioning.EnsureUserWithRolesAsync(u.Id, u.Username, u.DisplayName, u.Roles, ActorContext.System, "BOOTSTRAP_DEV_SEED", ct);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Seeding {u.Username} failed: {result.Error!.Code} {result.Error.Message}");
            }
        }

        log.WriteLine($"dev seed: {file.Users.Count} users ensured (audited)");
    }

    private DbContextOptions<T> Options<T>(string schema)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseMfopPostgres(ownerConnectionString, schema).Options;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record DevUsersFile(List<DevUser> Users);

    private sealed record DevUser(Guid Id, string Username, string DisplayName, List<string> Roles);
}
