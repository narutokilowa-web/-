using System.Text.Json;
using Mfop.Access;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Migrator;

/// <summary>
/// Applies module migrations as the owner role and then grants runtime privileges.
/// The runtime role (mfop_app) gets SELECT/INSERT/UPDATE only — never DELETE (nothing operational is deleted).
/// </summary>
public sealed class MigrationRunner(string ownerConnectionString, TextWriter log)
{
    private static readonly string[] ModuleSchemas = [AccessModule.Info.Schema];

    public async Task MigrateAsync(CancellationToken ct)
    {
        await using (var access = CreateAccessContext())
        {
            await access.Database.MigrateAsync(ct);
            log.WriteLine("access: migrations applied");
        }

        await ApplyGrantsAsync(ct);
    }

    private async Task ApplyGrantsAsync(CancellationToken ct)
    {
        await using var db = CreateAccessContext();
        foreach (var schema in ModuleSchemas)
        {
            // Schema names come from code constants, not user input.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"""
                GRANT USAGE ON SCHEMA {schema} TO mfop_app, mfop_readonly;
                GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA {schema} TO mfop_app;
                REVOKE DELETE, TRUNCATE ON ALL TABLES IN SCHEMA {schema} FROM mfop_app;
                GRANT SELECT ON ALL TABLES IN SCHEMA {schema} TO mfop_readonly;
                REVOKE ALL ON {schema}.__ef_migrations_history FROM mfop_app;
                """, ct);
#pragma warning restore EF1002
            log.WriteLine($"{schema}: grants applied (mfop_app: SELECT/INSERT/UPDATE, no DELETE)");
        }
    }

    /// <summary>
    /// Loads dev bootstrap users (fixed ids that match the Keycloak dev realm). Idempotent.
    /// M0 bootstrap only: from M1 user and role changes go through audited commands.
    /// </summary>
    public async Task SeedDevUsersAsync(string path, CancellationToken ct)
    {
        var file = JsonSerializer.Deserialize<DevUsersFile>(await File.ReadAllTextAsync(path, ct), JsonOptions)
                   ?? throw new InvalidOperationException($"Cannot read {path}");

        await using var db = CreateAccessContext();
        var now = DateTimeOffset.UtcNow;
        foreach (var u in file.Users)
        {
            var user = await db.Users.Include(x => x.RoleAssignments).SingleOrDefaultAsync(x => x.Id == u.Id, ct);
            if (user is null)
            {
                user = new User(u.Id, u.Id.ToString(), u.Username, u.DisplayName, now);
                db.Users.Add(user);
            }

            foreach (var role in u.Roles)
            {
                if (!user.RoleAssignments.Any(r => r.Role == role && r.IsActive(now)))
                {
                    db.RoleAssignments.Add(new RoleAssignment(Guid.CreateVersion7(), u.Id, role, null, "BOOTSTRAP_DEV_SEED", now));
                }
            }
        }

        await db.SaveChangesAsync(ct);
        log.WriteLine($"dev seed: {file.Users.Count} users ensured");
    }

    private AccessDbContext CreateAccessContext() =>
        new(new DbContextOptionsBuilder<AccessDbContext>()
            .UseMfopPostgres(ownerConnectionString, AccessModule.Info.Schema)
            .Options);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record DevUsersFile(List<DevUser> Users);

    private sealed record DevUser(Guid Id, string Username, string DisplayName, List<string> Roles);
}
