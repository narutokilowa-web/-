using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mfop.Migrator;
using Npgsql;

namespace Mfop.Api.Tests;

[Collection(nameof(DatabaseCollection))]
public class MeEndpointTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private HttpClient Client(string? sub = null)
    {
        var client = new TestApp(db).CreateClient();
        if (sub is not null)
        {
            client.DefaultRequestHeaders.Add(TestApp.SubHeader, sub);
        }

        return client;
    }

    public static TheoryData<string, string, string> DevUsers()
    {
        var data = new TheoryData<string, string, string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(DatabaseFixture.DevUsersPath));
        foreach (var u in doc.RootElement.GetProperty("users").EnumerateArray())
        {
            data.Add(u.GetProperty("id").GetString()!, u.GetProperty("username").GetString()!,
                string.Join(",", u.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Order()));
        }

        return data;
    }

    [Fact]
    public async Task Liveness_and_readiness_are_healthy()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(DevUsers))]
    public async Task Me_returns_roles_from_our_database_for_every_seeded_user(string id, string username, string roles)
    {
        using var client = Client(id);
        var response = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(username, me.GetProperty("username").GetString());
        Assert.Equal(roles, string.Join(",", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Order()));
        Assert.Equal("database", me.GetProperty("rolesSource").GetString());
    }

    [Fact]
    public async Task Unknown_identity_is_provisioned_on_first_login_with_no_roles_and_audited()
    {
        var sub = Guid.NewGuid().ToString();
        using var client = Client(sub);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Empty(me.GetProperty("roles").EnumerateArray());
        Assert.Equal(sub, me.GetProperty("userId").GetString());

        // Second call links to the same record (no duplicate).
        var again = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(sub, again.GetProperty("userId").GetString());

        Assert.Equal(1L, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM access.users WHERE idp_subject = '{sub}'"));
        Assert.Equal(1L, await db.ScalarAsOwnerAsync<long>(
            $"SELECT count(*) FROM audit.audit_events WHERE action = 'user.provisioned' AND entity_id = '{sub}' AND actor_user_id = '{sub}'"));
    }

    [Fact]
    public async Task Provisioned_user_without_roles_cannot_use_role_gated_endpoints()
    {
        using var client = Client(Guid.NewGuid().ToString());
        var response = await client.GetAsync("/api/v1/assets");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("FORBIDDEN", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Disabled_user_is_forbidden_with_stable_code()
    {
        var id = Guid.CreateVersion7();
        await db.ExecuteAsOwnerAsync($"""
            INSERT INTO access.users (id, idp_subject, username, display_name, status, created_at, version)
            VALUES ('{id}', '{id}', 'disabled-{id:N}', 'Disabled', 'Disabled', now(), 0);
            """);
        using var client = Client(id.ToString());
        var response = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("USER_DISABLED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Revoked_role_is_not_returned()
    {
        var id = Guid.CreateVersion7();
        await db.ExecuteAsOwnerAsync($"""
            INSERT INTO access.users (id, idp_subject, username, display_name, status, created_at, version)
            VALUES ('{id}', '{id}', 'revoked-{id:N}', 'Revoked', 'Active', now(), 0);
            INSERT INTO access.role_assignments (id, user_id, role, grant_reason, granted_at, revoked_at, revoke_reason)
            VALUES ('{Guid.CreateVersion7()}', '{id}', 'SUPERVISOR', 'test', now() - interval '1 day', now() - interval '1 hour', 'test');
            INSERT INTO access.role_assignments (id, user_id, role, grant_reason, granted_at)
            VALUES ('{Guid.CreateVersion7()}', '{id}', 'INSPECTOR', 'test', now() - interval '1 day');
            """);
        using var client = Client(id.ToString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(["INSPECTOR"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    [Fact]
    public async Task Runtime_role_cannot_delete_rows()
    {
        await using var conn = new NpgsqlConnection(db.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM access.role_assignments", conn);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Migrator_and_dev_seed_are_idempotent()
    {
        var runner = new MigrationRunner(db.OwnerConnectionString, TextWriter.Null);
        await runner.MigrateAsync(CancellationToken.None);
        await runner.SeedDevUsersAsync(DatabaseFixture.DevUsersPath, CancellationToken.None);

        await using var conn = new NpgsqlConnection(db.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM access.role_assignments WHERE grant_reason = 'BOOTSTRAP_DEV_SEED'", conn);
        await using var auditCmd = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_events WHERE action = 'role.granted' AND reason = 'BOOTSTRAP_DEV_SEED'", conn);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        using var doc = JsonDocument.Parse(File.ReadAllText(DatabaseFixture.DevUsersPath));
        var expected = doc.RootElement.GetProperty("users").EnumerateArray().Sum(u => u.GetProperty("roles").GetArrayLength());
        Assert.Equal(expected, count);
        Assert.Equal(expected, (long)(await auditCmd.ExecuteScalarAsync())!); // bootstrap is audited exactly once
    }
}
