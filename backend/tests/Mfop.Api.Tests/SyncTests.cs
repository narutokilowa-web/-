using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Mfop.Api.Tests;

/// <summary>The single write path POST /sync/v1/push: per-command results, idempotency by command id, recorded rejections.</summary>
[Collection(nameof(DatabaseCollection))]
public class SyncTests(DatabaseFixture db)
{
    private TestApp App { get; } = new(db);

    private static object Push(params object[] commands) => new { commands, pendingCommands = 0 };

    private static object ReportFault(Guid commandId, Guid assetId, string category = "FLICKERING") => new
    {
        commandId,
        type = "ReportFault",
        deviceTime = DateTimeOffset.UtcNow,
        payload = ApiHelpers.SampleFault(assetId, category),
    };

    private async Task<long> CountAsync(string sql) => await db.ScalarAsOwnerAsync<long>(sql);

    private async Task<(string Result, string? Code)> ReceiptAsync(Guid commandId)
    {
        await using var conn = new NpgsqlConnection(db.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT result, result_code FROM sync.sync_commands WHERE command_id = @c", conn);
        cmd.Parameters.AddWithValue("c", commandId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    private static List<JsonElement> Results(JsonElement body) => body.GetProperty("results").EnumerateArray().ToList();

    [Fact]
    public async Task A_command_is_applied_once_and_a_resend_is_reported_as_duplicate()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var supervisor = await App.CreateUserAsync("SUPERVISOR");
        var device = await App.ActiveDeviceAsync(supervisor);
        using var phone = App.FieldAppClientFor(supervisor, device);
        var commandId = Guid.NewGuid();

        var first = await (await phone.PostAsJsonAsync("/sync/v1/push", Push(ReportFault(commandId, assetId)))).Content.ReadFromJsonAsync<JsonElement>();
        var applied = Results(first).Single();
        Assert.Equal("APPLIED", applied.GetProperty("result").GetString());
        Assert.Equal("CREATED", applied.GetProperty("code").GetString());
        var faultId = applied.GetProperty("entityId").GetGuid();

        // Same command id, same payload (e.g. the phone never saw the first response): nothing is applied twice.
        var again = await (await phone.PostAsJsonAsync("/sync/v1/push", Push(ReportFault(commandId, assetId)))).Content.ReadFromJsonAsync<JsonElement>();
        var duplicate = Results(again).Single();
        Assert.Equal("DUPLICATE", duplicate.GetProperty("result").GetString());
        Assert.Equal(faultId, duplicate.GetProperty("entityId").GetGuid());
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM faults.fault_confirmations c JOIN faults.faults f ON f.id = c.fault_id WHERE f.asset_id = '{assetId}'"));

        // The audit events carry the phone's command id and device id.
        Assert.Equal(["fault.reported", "street_light.operational_status_changed", "asset_status_event.recorded"], await db.AuditActionsForCommandAsync(commandId));
        Assert.Equal(3, await CountAsync($"SELECT count(*) FROM audit.audit_events WHERE command_id = '{commandId}' AND device_id = '{device}'"));
        Assert.Equal(("APPLIED", "CREATED"), await ReceiptAsync(commandId));

        // Same id reused for a different command: rejected, never applied.
        var conflict = await (await phone.PostAsJsonAsync("/sync/v1/push", Push(ReportFault(commandId, assetId, "DAY_BURNING")))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("COMMAND_ID_CONFLICT", Results(conflict).Single().GetProperty("code").GetString());
    }

    [Fact]
    public async Task Each_command_gets_its_own_result_in_order_and_rejections_are_recorded_without_audit_events()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var supervisor = await App.CreateUserAsync("SUPERVISOR");
        var device = await App.ActiveDeviceAsync(supervisor);
        using var phone = App.FieldAppClientFor(supervisor, device);
        var (ok, unknown, invalid) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var body = await (await phone.PostAsJsonAsync("/sync/v1/push", Push(
            ReportFault(ok, assetId),
            new { commandId = unknown, type = "LaunchRocket", payload = new { } },
            new { commandId = invalid, type = "ReportFault", payload = new { assetId, category = "NOPE", powerAtInspection = "UNKNOWN" } })))
            .Content.ReadFromJsonAsync<JsonElement>();

        var results = Results(body);
        Assert.Equal([ok, unknown, invalid], results.Select(r => r.GetProperty("commandId").GetGuid()));
        Assert.Equal(["APPLIED", "REJECTED", "REJECTED"], results.Select(r => r.GetProperty("result").GetString()));
        Assert.Equal("UNKNOWN_COMMAND", results[1].GetProperty("code").GetString());
        Assert.Equal("INVALID_FAULT", results[2].GetProperty("code").GetString());

        Assert.Equal(("REJECTED", "UNKNOWN_COMMAND"), await ReceiptAsync(unknown));
        Assert.Equal(("REJECTED", "INVALID_FAULT"), await ReceiptAsync(invalid));
        Assert.Empty(await db.AuditActionsForCommandAsync(unknown));
        Assert.Empty(await db.AuditActionsForCommandAsync(invalid));

        // Re-sending a rejected command returns the same rejection (the phone shows it; nothing is retried forever).
        var replay = await (await phone.PostAsJsonAsync("/sync/v1/push", Push(new { commandId = unknown, type = "LaunchRocket", payload = new { } })))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(("REJECTED", "UNKNOWN_COMMAND"), (Results(replay)[0].GetProperty("result").GetString()!, Results(replay)[0].GetProperty("code").GetString()));
    }

    [Fact]
    public async Task Every_command_is_authorized_by_its_own_permission()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var tech = await App.CreateUserAsync("TECHNICIAN");
        var device = await App.ActiveDeviceAsync(tech);
        using var phone = App.FieldAppClientFor(tech, device);
        var commandId = Guid.NewGuid();
        var body = await (await phone.PostAsJsonAsync("/sync/v1/push", Push(ReportFault(commandId, assetId)))).Content.ReadFromJsonAsync<JsonElement>();
        var result = Results(body).Single();
        Assert.Equal("REJECTED", result.GetProperty("result").GetString());
        Assert.Equal("FORBIDDEN", result.GetProperty("code").GetString()); // fault.report is SUPERVISOR-only in DEMO 1
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));
    }

    [Fact]
    public async Task N8_push_from_a_revoked_device_applies_nothing()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var supervisor = await App.CreateUserAsync("SUPERVISOR");
        var device = await App.ActiveDeviceAsync(supervisor);
        using var admin = App.ClientFor(DevUsers.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/devices/{device}/revoke", new { reason = "انتهاء الخدمة" })).EnsureSuccessStatusCode();

        using var phone = App.FieldAppClientFor(supervisor, device);
        var commandId = Guid.NewGuid();
        var response = await phone.PostAsJsonAsync("/sync/v1/push", Push(ReportFault(commandId, assetId)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("DEVICE_REVOKED", await response.ProblemCodeAsync());
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM sync.sync_commands WHERE command_id = '{commandId}'"));
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));
    }

    [Fact]
    public async Task Push_and_pull_need_a_device_and_are_shown_as_last_sync_on_the_devices_page()
    {
        var tech = await App.CreateUserAsync("TECHNICIAN");
        using var web = App.ClientFor(tech);
        Assert.Equal("DEVICE_REQUIRED", await (await web.PostAsJsonAsync("/sync/v1/push", Push())).ProblemCodeAsync());
        Assert.Equal("DEVICE_REQUIRED", await (await web.GetAsync("/sync/v1/pull")).ProblemCodeAsync());

        var device = await App.ActiveDeviceAsync(tech);
        using var phone = App.FieldAppClientFor(tech, device);
        (await phone.PostAsJsonAsync("/sync/v1/push", new { commands = Array.Empty<object>(), pendingCommands = 3 })).EnsureSuccessStatusCode();
        (await phone.GetAsync("/sync/v1/pull")).EnsureSuccessStatusCode();
        Assert.Equal("BATCH_TOO_LARGE", await (await phone.PostAsJsonAsync("/sync/v1/push",
            Push(Enumerable.Range(0, 101).Select(_ => (object)new { commandId = Guid.NewGuid(), type = "x", payload = new { } }).ToArray()))).ProblemCodeAsync());

        using var admin = App.ClientFor(DevUsers.Admin);
        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/devices")).EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == device);
        Assert.Equal(JsonValueKind.String, row.GetProperty("lastPullAt").ValueKind);
        Assert.Equal(JsonValueKind.String, row.GetProperty("lastPushAt").ValueKind);
        Assert.Equal(3, row.GetProperty("lastReportedPendingCommands").GetInt32());
    }

    [Fact]
    public async Task The_app_role_cannot_rewrite_sync_receipts()
    {
        await using var conn = new NpgsqlConnection(db.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE sync.sync_commands SET result = 'APPLIED'", conn);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState); // insufficient_privilege
    }
}
