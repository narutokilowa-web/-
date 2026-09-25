using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mfop.AuditVerify;

namespace Mfop.Api.Tests;

/// <summary>DEMO 1 steps 2–5 through the public API: register → separate approval → asset code, tag, version, history.</summary>
[Collection(nameof(DatabaseCollection))]
public class RegistrationFlowTests(DatabaseFixture db) : IAsyncLifetime
{
    private TestApp App { get; } = new(db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    [Fact]
    public async Task Register_submit_approve_issues_code_tag_version_and_history()
    {
        var (registrationId, assetId) = await App.RegisterAndApproveAsync();
        using var approver = App.ClientFor(DevUsers.Approver);

        var registration = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{registrationId}");
        Assert.Equal("APPROVED", registration.GetProperty("state").GetString());
        Assert.Equal(DevUsers.Approver, registration.GetProperty("decidedBy").GetString());

        var asset = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}");
        Assert.Matches("^SL-[0-9]{6}$", asset.GetProperty("assetCode").GetString());
        Assert.Equal("ACTIVE", asset.GetProperty("lifecycleState").GetString());
        Assert.Equal("OPERATIONAL", asset.GetProperty("operationalStatus").GetString());
        Assert.Equal("WEB", asset.GetProperty("captureChannel").GetString());
        Assert.Equal(1, asset.GetProperty("currentVersionNo").GetInt32());

        var tag = asset.GetProperty("tag");
        var human = tag.GetProperty("humanCode").GetString()!;
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{8}\\.[0-9A-HJKMNP-TV-Z]{2}$", human);
        Assert.Equal($"http://web.test/t/{human}", tag.GetProperty("qrPayload").GetString());

        // QR resolves back to the asset; a wrong check code does not.
        var resolved = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/tags/{human}");
        Assert.Equal(assetId, resolved.GetProperty("assetId").GetGuid());
        var forged = human[..^2] + (human[^1] == 'A' ? "BB" : "AA");
        Assert.Equal(HttpStatusCode.NotFound, (await approver.GetAsync($"/api/v1/tags/{forged}")).StatusCode);

        var versions = asset.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());
        Assert.Equal(64, versions[0].GetProperty("snapshotHash").GetString()!.Length);

        var history = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}/history");
        var actions = history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Equal(
            ["registration.drafted", "street_light.created", "registration.submitted", "registration.approved", "street_light.activated", "tag.generated_and_bound", "asset_version.created"],
            actions);

        var activated = history.EnumerateArray().Single(e => e.GetProperty("action").GetString() == "street_light.activated");
        var lifecycle = activated.GetProperty("changes").GetProperty("lifecycle_state");
        Assert.Equal("PENDING", lifecycle.GetProperty("old").GetString());
        Assert.Equal("ACTIVE", lifecycle.GetProperty("new").GetString());
        Assert.Equal("مسجّل تجريبي", history[0].GetProperty("actorName").GetString());
        Assert.Equal("معتمد تجريبي", activated.GetProperty("actorName").GetString());
        Assert.Equal("موافق", history.EnumerateArray().Single(e => e.GetProperty("action").GetString() == "registration.approved").GetProperty("reason").GetString());

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task Asset_codes_are_sequential_and_unique()
    {
        var (_, a1) = await App.RegisterAndApproveAsync();
        var (_, a2) = await App.RegisterAndApproveAsync();
        using var client = App.ClientFor(DevUsers.Auditor);
        var c1 = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{a1}")).GetProperty("assetCode").GetString()!;
        var c2 = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{a2}")).GetProperty("assetCode").GetString()!;
        Assert.True(int.Parse(c2[3..]) > int.Parse(c1[3..]));
    }

    [Fact]
    public async Task N1_a_registrar_who_later_became_approver_cannot_approve_their_own_registration()
    {
        // Static SoD forbids holding REGISTRAR and APPROVER together, so the realistic path is a role change over time.
        var sub = Guid.NewGuid().ToString();
        using var user = App.ClientFor(sub);
        await user.GetAsync("/api/v1/me"); // provisioned on first login
        await db.ExecuteAsOwnerAsync($"INSERT INTO access.role_assignments (id, user_id, role, grant_reason, granted_at) VALUES ('{Guid.NewGuid()}', '{sub}', 'REGISTRAR', 'test', now() - interval '1 hour')");

        var id = await user.CreateRegistrationAsync();
        (await user.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();

        await db.ExecuteAsOwnerAsync($"""
            UPDATE access.role_assignments SET revoked_at = now(), revoke_reason = 'test' WHERE user_id = '{sub}' AND role = 'REGISTRAR';
            INSERT INTO access.role_assignments (id, user_id, role, grant_reason, granted_at) VALUES ('{Guid.NewGuid()}', '{sub}', 'APPROVER', 'test', now() - interval '1 second');
            """);

        var response = await user.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "self" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SOD_VIOLATION", await response.ProblemCodeAsync());

        using var auditor = App.ClientFor(DevUsers.Auditor);
        var registration = await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{id}");
        Assert.Equal("SUBMITTED", registration.GetProperty("state").GetString()); // nothing changed
    }

    [Fact]
    public async Task Registrar_cannot_approve_even_through_the_api()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        var id = await registrar.CreateRegistrationAsync();
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();
        var response = await registrar.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Return_then_correct_then_resubmit_records_old_and_new_values()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        using var approver = App.ClientFor(DevUsers.Approver);
        var id = await registrar.CreateRegistrationAsync();
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();

        Assert.Equal("NOTE_REQUIRED", await (await approver.PostCommandAsync($"/api/v1/registrations/{id}/return", new { note = "" })).ProblemCodeAsync());
        (await approver.PostCommandAsync($"/api/v1/registrations/{id}/return", new { note = "العنوان غير صحيح" })).EnsureSuccessStatusCode();

        var update = await registrar.PostAsJsonAsync($"/api/v1/registrations/{id}/update", ApiHelpers.SampleRegistration(address: "شارع مصحح 2"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();
        (await approver.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "ok" })).EnsureSuccessStatusCode();

        var registration = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{id}");
        Assert.Equal(1, registration.GetProperty("returnCount").GetInt32());
        var history = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{registration.GetProperty("assetId").GetGuid()}/history");
        var updated = history.EnumerateArray().Single(e => e.GetProperty("action").GetString() == "street_light.attributes_updated");
        var address = updated.GetProperty("changes").GetProperty("address_text");
        Assert.Equal("شارع تجريبي 1", address.GetProperty("old").GetString());
        Assert.Equal("شارع مصحح 2", address.GetProperty("new").GetString());
        Assert.Contains(history.EnumerateArray(), e => e.GetProperty("action").GetString() == "registration.returned"
                                                      && e.GetProperty("reason").GetString() == "العنوان غير صحيح");
    }

    [Fact]
    public async Task Rejected_registration_is_terminal_and_the_asset_never_becomes_active()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        using var approver = App.ClientFor(DevUsers.Approver);
        var id = await registrar.CreateRegistrationAsync();
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();
        (await approver.PostCommandAsync($"/api/v1/registrations/{id}/reject", new { note = "عمود مكرر" })).EnsureSuccessStatusCode();

        var again = await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("INVALID_TRANSITION", await again.ProblemCodeAsync());

        var registration = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{id}");
        var asset = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{registration.GetProperty("assetId").GetGuid()}");
        Assert.Equal("REJECTED", asset.GetProperty("lifecycleState").GetString());
        Assert.Equal(JsonValueKind.Null, asset.GetProperty("assetCode").ValueKind);
    }

    [Fact]
    public async Task Discarded_draft_cannot_be_submitted()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        var id = await registrar.CreateRegistrationAsync();
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/discard")).EnsureSuccessStatusCode();
        Assert.Equal("INVALID_TRANSITION", await (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).ProblemCodeAsync());
    }

    [Fact]
    public async Task Approving_a_draft_that_was_never_submitted_is_an_invalid_transition()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        using var approver = App.ClientFor(DevUsers.Approver);
        var id = await registrar.CreateRegistrationAsync();
        var response = await approver.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "x" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("INVALID_TRANSITION", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invalid_attributes_are_rejected_with_details()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        var bad = await registrar.PostAsJsonAsync("/api/v1/registrations", ApiHelpers.SampleRegistration(lat: 95m));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("INVALID_ATTRIBUTES", await bad.ProblemCodeAsync());

        var noProject = await registrar.PostAsJsonAsync("/api/v1/registrations", ApiHelpers.SampleRegistration(reason: "NEW_INSTALLATION"));
        Assert.Equal("PROJECT_REF_REQUIRED", await noProject.ProblemCodeAsync());

        var tooPrecise = await registrar.PostAsJsonAsync("/api/v1/registrations", ApiHelpers.SampleRegistration(lat: 33.1234567m));
        Assert.Equal("INVALID_ATTRIBUTES", await tooPrecise.ProblemCodeAsync());

        var solarFieldsOnGrid = await registrar.PostAsJsonAsync("/api/v1/registrations", new
        {
            reason = "BASELINE_SURVEY",
            attributes = new
            {
                latitude = 33.5m, longitude = 36.2m, powerSource = "GRID", responsibleParty = "MUNICIPALITY",
                installedOnPrecision = "UNKNOWN", observedStatus = "UNKNOWN", solarPanelW = 100,
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, solarFieldsOnGrid.StatusCode);
        var details = (await solarFieldsOnGrid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("details");
        Assert.True(details.TryGetProperty("solar", out _));
    }

    [Fact]
    public async Task A_registrar_cannot_see_or_act_on_another_registrars_registration()
    {
        using var owner = App.ClientFor(DevUsers.Registrar);
        var id = await owner.CreateRegistrationAsync();

        var otherSub = Guid.NewGuid().ToString();
        using var other = App.ClientFor(otherSub);
        await other.GetAsync("/api/v1/me");
        await db.ExecuteAsOwnerAsync($"INSERT INTO access.role_assignments (id, user_id, role, grant_reason, granted_at) VALUES ('{Guid.NewGuid()}', '{otherSub}', 'REGISTRAR', 'test', now() - interval '1 hour')");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/registrations/{id}")).StatusCode);
        var submit = await other.PostCommandAsync($"/api/v1/registrations/{id}/submit");
        Assert.Equal(HttpStatusCode.Forbidden, submit.StatusCode);
        Assert.Equal("NOT_REGISTRAR", await submit.ProblemCodeAsync());
        var list = await other.GetFromJsonAsync<JsonElement>("/api/v1/registrations");
        Assert.DoesNotContain(list.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Allowed_commands_reflect_role_state_and_maker_checker()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        using var approver = App.ClientFor(DevUsers.Approver);
        var id = await registrar.CreateRegistrationAsync();

        static async Task<string[]> Allowed(HttpClient c, Guid id) =>
            [.. (await c.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{id}")).GetProperty("allowedCommands").EnumerateArray().Select(x => x.GetString()!).Order()];

        Assert.Equal(["DiscardDraft", "SubmitRegistration", "UpdateRegistrationDraft"], await Allowed(registrar, id));
        Assert.Empty(await Allowed(approver, id));
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();
        Assert.Empty(await Allowed(registrar, id));
        Assert.Equal(["ApproveRegistration", "RejectRegistration", "ReturnRegistration"], await Allowed(approver, id));
    }
}
