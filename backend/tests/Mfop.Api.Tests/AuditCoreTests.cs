using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Assets.Domain;
using Mfop.Assets.Infrastructure;
using Mfop.AuditVerify;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Json;
using Mfop.Kernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Mfop.Api.Tests;

[Collection(nameof(DatabaseCollection))]
public class AuditCoreTests(DatabaseFixture db)
{
    private TestApp App { get; } = new(db);

    private async Task<string[]> ActionsForCommandAsync(Guid commandId)
    {
        await using var conn = new NpgsqlConnection(db.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT action FROM audit.audit_events WHERE command_id = @c ORDER BY seq", conn);
        cmd.Parameters.AddWithValue("c", commandId);
        var actions = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            actions.Add(r.GetString(0));
        }

        return [.. actions];
    }

    [Fact]
    public async Task Every_M1_command_writes_its_audit_events_with_the_command_id()
    {
        async Task<string[]> Run(string sub, Func<HttpClient, Task<HttpResponseMessage>> call)
        {
            var key = Guid.NewGuid();
            using var client = App.ClientFor(sub, key);
            (await call(client)).EnsureSuccessStatusCode();
            return await ActionsForCommandAsync(key);
        }

        Guid id = default;
        Assert.Equal(["registration.drafted", "street_light.created"], await Run(DevUsers.Registrar, async c =>
        {
            var r = await c.PostAsJsonAsync("/api/v1/registrations", ApiHelpers.SampleRegistration());
            id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            return r;
        }));
        Assert.Equal(["registration.updated", "street_light.attributes_updated"],
            await Run(DevUsers.Registrar, c => c.PostAsJsonAsync($"/api/v1/registrations/{id}/update", ApiHelpers.SampleRegistration(address: "x"))));
        Assert.Equal(["registration.submitted"], await Run(DevUsers.Registrar, c => c.PostCommandAsync($"/api/v1/registrations/{id}/submit")));
        Assert.Equal(["registration.returned"], await Run(DevUsers.Approver, c => c.PostCommandAsync($"/api/v1/registrations/{id}/return", new { note = "fix" })));
        Assert.Equal(["registration.submitted"], await Run(DevUsers.Registrar, c => c.PostCommandAsync($"/api/v1/registrations/{id}/submit")));
        Assert.Equal(["registration.approved", "street_light.activated", "tag.generated_and_bound", "asset_version.created"],
            await Run(DevUsers.Approver, c => c.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "ok" })));

        using var registrar = App.ClientFor(DevUsers.Registrar);
        var id2 = await registrar.CreateRegistrationAsync();
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id2}/submit")).EnsureSuccessStatusCode();
        Assert.Equal(["registration.rejected", "street_light.registration_rejected"],
            await Run(DevUsers.Approver, c => c.PostCommandAsync($"/api/v1/registrations/{id2}/reject", new { note = "no" })));

        var id3 = await registrar.CreateRegistrationAsync();
        Assert.Equal(["registration.discarded", "street_light.registration_discarded"],
            await Run(DevUsers.Registrar, c => c.PostCommandAsync($"/api/v1/registrations/{id3}/discard")));

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task A_rejected_command_writes_nothing()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        var id = await registrar.CreateRegistrationAsync();
        var key = Guid.NewGuid();
        using var approver = App.ClientFor(DevUsers.Approver, key);
        var response = await approver.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "not submitted" });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty(await ActionsForCommandAsync(key));
    }

    [Fact]
    public async Task Unit_of_work_refuses_a_change_without_an_audit_record_and_rolls_back()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var assets = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<AuditMissingException>(() => uow.ExecuteAsync(CommandEnvelope.For("Test", ActorContext.System), _ =>
        {
            assets.StreetLights.Add(new StreetLight(id, ValidAttributes(), "WEB", DateTimeOffset.UtcNow));
            return Task.FromResult(Result<bool>.Ok(true)); // no audit.Record(...)
        }, CancellationToken.None));

        Assert.Equal(0L, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM assets.street_lights WHERE id = '{id}'"));
    }

    [Fact]
    public async Task Failure_or_exception_after_a_change_persists_neither_the_change_nor_audit()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var assets = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<Mfop.Kernel.Auditing.IAuditTrail>();
        var failedId = Guid.NewGuid();
        var thrownId = Guid.NewGuid();
        var command = CommandEnvelope.For("Test", ActorContext.System);

        var failed = await uow.ExecuteAsync(command, _ =>
        {
            var light = new StreetLight(failedId, ValidAttributes(), "WEB", DateTimeOffset.UtcNow);
            assets.StreetLights.Add(light);
            audit.Record(light, "test.created");
            return Task.FromResult(Result<bool>.Failure(Error.Validation("TEST_FAILURE", "forced")));
        }, CancellationToken.None);
        Assert.False(failed.IsSuccess);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync<bool>(CommandEnvelope.For("Test", ActorContext.System), _ =>
        {
            var light = new StreetLight(thrownId, ValidAttributes(), "WEB", DateTimeOffset.UtcNow);
            assets.StreetLights.Add(light);
            audit.Record(light, "test.created");
            throw new InvalidOperationException("boom");
        }, CancellationToken.None));

        Assert.Equal(0L, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM assets.street_lights WHERE id IN ('{failedId}', '{thrownId}')"));
        Assert.Equal(0L, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM audit.audit_events WHERE entity_id IN ('{failedId}', '{thrownId}')"));
    }

    [Theory]
    [InlineData("UPDATE audit.audit_events SET reason = 'x' WHERE seq = 1")]
    [InlineData("DELETE FROM audit.audit_events WHERE seq = 1")]
    [InlineData("TRUNCATE audit.audit_events")]
    [InlineData("UPDATE assets.asset_versions SET reason = 'x'")]
    [InlineData("DELETE FROM assets.street_lights")]
    public async Task N11_the_application_role_cannot_change_or_delete_append_only_data(string sql)
    {
        await using var conn = new NpgsqlConnection(db.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task Even_the_owner_role_is_stopped_by_the_append_only_trigger()
    {
        await App.RegisterAndApproveAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsOwnerAsync("UPDATE audit.audit_events SET reason = 'x' WHERE seq = 1"));
        Assert.Contains("append-only", ex.MessageText);
        var ex2 = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsOwnerAsync("DELETE FROM assets.asset_versions"));
        Assert.Contains("append-only", ex2.MessageText);
    }

    [Fact]
    public async Task N12_a_manually_tampered_audit_row_is_detected_at_its_seq()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var seq = await db.ScalarAsOwnerAsync<long>($"SELECT seq FROM audit.audit_events WHERE entity_id = '{assetId}' AND action = 'street_light.activated'");
        var original = await db.ScalarAsOwnerAsync<string>($"SELECT changes::text FROM audit.audit_events WHERE seq = {seq}");
        try
        {
            // A privileged DBA can do this (trigger disabled). The verifier must catch it.
            await AsOwnerWithoutTriggerAsync("audit.audit_events", "audit_events_no_update_delete",
                $"UPDATE audit.audit_events SET changes = jsonb_set(changes, '{{lifecycle_state,new}}', '\"TAMPERED\"') WHERE seq = {seq}");

            var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
            Assert.False(report.Ok);
            var first = report.Findings[0];
            Assert.Equal("EVENT_CONTENT_CHANGED", first.Code);
            Assert.Equal(seq, first.Seq);
            Assert.Equal("street_light", first.EntityType);
            Assert.Equal(assetId.ToString(), first.EntityId);
        }
        finally
        {
            await AsOwnerWithoutTriggerAsync("audit.audit_events", "audit_events_no_update_delete",
                $"UPDATE audit.audit_events SET changes = '{original.Replace("'", "''")}'::jsonb WHERE seq = {seq}");
        }

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task A_directly_edited_asset_row_is_detected()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        try
        {
            await db.ExecuteAsOwnerAsync($"UPDATE assets.street_lights SET latitude = latitude + 0.001 WHERE id = '{assetId}'");
            var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
            var finding = Assert.Single(report.Findings);
            Assert.Equal("ASSET_ROW_CHANGED", finding.Code);
            Assert.Equal(assetId.ToString(), finding.EntityId);
            Assert.Contains("latitude", finding.Message);
        }
        finally
        {
            await db.ExecuteAsOwnerAsync($"UPDATE assets.street_lights SET latitude = latitude - 0.001 WHERE id = '{assetId}'");
        }

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task A_tampered_approved_snapshot_is_detected()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var original = await db.ScalarAsOwnerAsync<string>($"SELECT snapshot::text FROM assets.asset_versions WHERE asset_id = '{assetId}'");
        try
        {
            await AsOwnerWithoutTriggerAsync("assets.asset_versions", "asset_versions_no_update_delete",
                $"UPDATE assets.asset_versions SET snapshot = jsonb_set(snapshot, '{{power_source}}', '\"SOLAR\"') WHERE asset_id = '{assetId}'");
            var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
            Assert.Contains(report.Findings, f => f.Code == "SNAPSHOT_HASH_MISMATCH" && f.EntityType == "asset_version");
            Assert.Contains(report.Findings, f => f.Code == "ASSET_ROW_CHANGED" && f.EntityId == assetId.ToString());
        }
        finally
        {
            await AsOwnerWithoutTriggerAsync("assets.asset_versions", "asset_versions_no_update_delete",
                $"UPDATE assets.asset_versions SET snapshot = '{original.Replace("'", "''")}'::jsonb WHERE asset_id = '{assetId}'");
        }

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task A_deleted_audit_event_is_detected_as_a_gap()
    {
        var (registrationId, _) = await App.RegisterAndApproveAsync();
        var seq = await db.ScalarAsOwnerAsync<long>($"SELECT seq FROM audit.audit_events WHERE entity_id = '{registrationId}' AND action = 'registration.submitted'");
        await db.ExecuteAsOwnerAsync($"CREATE TABLE IF NOT EXISTS public.audit_backup AS SELECT * FROM audit.audit_events WHERE false; INSERT INTO public.audit_backup SELECT * FROM audit.audit_events WHERE seq = {seq};");
        try
        {
            await AsOwnerWithoutTriggerAsync("audit.audit_events", "audit_events_no_update_delete", $"DELETE FROM audit.audit_events WHERE seq = {seq}");
            var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
            Assert.Equal("SEQ_GAP", report.Findings[0].Code);
            Assert.Equal(seq + 1, report.Findings[0].Seq);
        }
        finally
        {
            await db.ExecuteAsOwnerAsync($"INSERT INTO audit.audit_events SELECT * FROM public.audit_backup WHERE seq = {seq}; DELETE FROM public.audit_backup;");
        }

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public void Kernel_and_verifier_canonical_json_are_byte_identical()
    {
        var node = new JsonObject
        {
            ["z"] = "آخر",
            ["a"] = new JsonObject { ["old"] = null, ["new"] = "قيمة \"مقتبسة\" \\ \n\t\u0001" },
            ["n"] = 42,
            ["neg"] = -7,
            ["b"] = true,
            ["arr"] = new JsonArray("x", null, 3, new JsonObject { ["k"] = "v" }),
            ["emoji"] = "💡",
            ["B"] = "upper-before-lower",
        };
        var element = JsonSerializer.SerializeToElement(node);
        Assert.Equal(CanonicalJson.Serialize(node), Canonical.Bytes(element));
        Assert.Equal(CanonicalJson.Sha256Hex(node), Canonical.Sha256Hex(element));
    }

    private async Task AsOwnerWithoutTriggerAsync(string table, string trigger, string sql) =>
        await db.ExecuteAsOwnerAsync($"ALTER TABLE {table} DISABLE TRIGGER {trigger}; {sql}; ALTER TABLE {table} ENABLE TRIGGER {trigger};");

    private static StreetLightAttributes ValidAttributes() =>
        new(33.5m, 36.3m, PowerSources.Grid, ResponsibleParties.Municipality, InstallDatePrecisions.Unknown, OperationalStatuses.Unknown);
}
