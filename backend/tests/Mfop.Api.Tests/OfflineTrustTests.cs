using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Access.Contracts;

namespace Mfop.Api.Tests;

/// <summary>
/// M4 device trust (docs/lean-pilot/05 §5.4–5.7): device-key signatures, minimum app version, configurable offline grants and
/// the server's grant-window check on trusted time. The server clock is moved forward to play the hours a phone spends offline.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class OfflineTrustTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly ShiftableClock _clock = new();

    private TestApp App => _app ??= new TestApp(db, clock: _clock);

    private TestApp? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>A supervisor with an approved, keyed phone: ReportFault is a simple phone command without a work order.</summary>
    private async Task<(string User, Guid Device, TestPhone Phone, Guid AssetId)> PhoneAsync()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        var user = await App.CreateUserAsync("SUPERVISOR");
        var device = await App.ActiveDeviceAsync(user);
        var phone = TestPhone.For(device)!;
        phone.SignatureClock = _clock.GetUtcNow;
        return (user, device, phone, assetId);
    }

    private static JsonObject Fault(Guid assetId, string category = "FLICKERING") => new()
    {
        ["commandId"] = Guid.CreateVersion7().ToString("D"),
        ["type"] = "ReportFault",
        ["deviceTime"] = DateTimeOffset.UtcNow.ToString("O"),
        ["payload"] = JsonSerializer.SerializeToNode(ApiHelpers.SampleFault(assetId, category)),
    };

    /// <summary>A command as the phone records it in its outbox: the grant, boot and monotonic clock of that moment.</summary>
    private JsonObject Offline(TestPhone phone, Guid assetId, string category = "FLICKERING")
    {
        var c = Fault(assetId, category);
        c["deviceTime"] = _clock.GetUtcNow().ToString("O");
        c["grantId"] = phone.GrantId?.ToString("D");
        c["elapsedMs"] = phone.ElapsedMs;
        c["bootId"] = phone.BootId;
        return c;
    }

    private static async Task<JsonElement> PushAsync(HttpClient client, params JsonObject[] commands)
    {
        var response = await client.PostAsJsonAsync("/sync/v1/push", new { commands, pendingCommands = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0];
    }

    private static async Task<JsonElement> GrantAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/sync/v1/grant", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<long> FlagsAsync(string code, Guid subject) =>
        db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM rules.flags WHERE rule_code = '{code}' AND subject_id = '{subject}'");

    private async Task SetPolicyAsync(string key, string value)
    {
        using var admin = App.ClientFor(DevUsers.Admin);
        var response = await admin.PostAsJsonAsync($"/api/v1/settings/policy/{key}", new { value, reason = "اختبار M4" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- device-key signing ----

    [Fact]
    public async Task Push_must_be_signed_by_the_registered_device_key_with_a_fresh_single_use_nonce()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { commands = new[] { Fault(assetId) }, pendingCommands = 0 });

        async Task<HttpResponseMessage> Send(byte[] payload, Action<HttpRequestMessage> sign)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/sync/v1/push") { Content = new ByteArrayContent(payload) };
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            sign(request);
            return await client.SendAsync(request);
        }

        void With(HttpRequestMessage r, (string Timestamp, string Nonce, string Signature) s)
        {
            r.Headers.Add(TrustHttp.SignatureTimestampHeader, s.Timestamp);
            r.Headers.Add(TrustHttp.SignatureNonceHeader, s.Nonce);
            r.Headers.Add(TrustHttp.SignatureHeader, s.Signature);
        }

        phone.Sign = false;
        Assert.Equal(TrustErrorCodes.SignatureRequired, await (await Send(body, _ => { })).ProblemCodeAsync());

        // A copied access token without the phone's Keystore key.
        using (var thief = new TestPhone())
        {
            Assert.Equal(TrustErrorCodes.SignatureInvalid, await (await Send(body, r => With(r, thief.SignRequest("POST", "/sync/v1/push", body)))).ProblemCodeAsync());
        }

        // Body changed after signing.
        var tampered = JsonSerializer.SerializeToUtf8Bytes(new { commands = new[] { Fault(assetId, "DAY_BURNING") }, pendingCommands = 0 });
        Assert.Equal(TrustErrorCodes.SignatureInvalid, await (await Send(tampered, r => With(r, phone.SignRequest("POST", "/sync/v1/push", body)))).ProblemCodeAsync());

        // A signature made long ago (a recorded request played back later).
        var old = phone.SignRequest("POST", "/sync/v1/push", body, DateTimeOffset.UtcNow.AddHours(-2));
        Assert.Equal(TrustErrorCodes.SignatureExpired, await (await Send(body, r => With(r, old))).ProblemCodeAsync());

        Assert.Equal(0, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM sync.sync_commands WHERE device_id = '{device}'"));

        // The genuine request is accepted once; the same nonce again is a replay.
        phone.Sign = true;
        phone.GrantId = (await GrantAsync(App.FieldAppClientFor(user, device))).GetProperty("grantId").GetGuid();
        var withGrant = Fault(assetId);
        withGrant["grantId"] = phone.GrantId!.Value.ToString("D");
        var signedBody = JsonSerializer.SerializeToUtf8Bytes(new { commands = new[] { withGrant }, pendingCommands = 0 });
        var genuine = phone.SignRequest("POST", "/sync/v1/push", signedBody);
        var ok = await Send(signedBody, r => With(r, genuine));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("APPLIED", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0].GetProperty("result").GetString());
        Assert.Equal(TrustErrorCodes.NonceReused, await (await Send(signedBody, r => With(r, genuine))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Registration_requires_a_valid_device_public_key()
    {
        var user = await App.CreateUserAsync("TECHNICIAN");
        using var client = App.FieldAppClientFor(user, null);
        foreach (var key in new[] { null, "not-a-key", Convert.ToBase64String(RSA.Create(2048).ExportSubjectPublicKeyInfo()) })
        {
            var response = await client.PostAsJsonAsync("/sync/v1/devices/register", new { platform = "ANDROID", model = "X", osVersion = "14", appVersion = "0.5.0", publicKey = key });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var details = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("details");
            Assert.True(details.TryGetProperty("publicKey", out _));
        }
    }

    // ---- minimum app version (policy) ----

    [Fact]
    public async Task An_app_older_than_the_minimum_version_gets_426_and_the_minimum_is_a_policy_the_admin_changes()
    {
        var (user, device, phone, _) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        phone.AppVersion = "0.4.9";
        var old = await client.GetAsync("/sync/v1/pull");
        Assert.Equal((HttpStatusCode)426, old.StatusCode);
        var problem = await old.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(TrustErrorCodes.UpgradeRequired, problem.GetProperty("code").GetString());
        Assert.Equal("0.5.0", problem.GetProperty("details").GetProperty("minAppVersion").GetString());

        phone.AppVersion = "0.5.0";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sync/v1/pull")).StatusCode);
        try
        {
            await SetPolicyAsync("min_app_version", "0.6.0");
            Assert.Equal((HttpStatusCode)426, (await client.GetAsync("/sync/v1/pull")).StatusCode);
        }
        finally
        {
            await SetPolicyAsync("min_app_version", "0.5.0");
        }
    }

    [Fact]
    public async Task Policies_are_validated_need_a_reason_and_are_audited()
    {
        using var admin = App.ClientFor(DevUsers.Admin);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings/policy");
        var keys = list.EnumerateArray().Select(p => p.GetProperty("key").GetString()).ToList();
        Assert.Contains("offline_grace_hours", keys);
        Assert.Contains("grant_window_tolerance_minutes", keys);
        Assert.Contains("app_lock_minutes", keys);
        Assert.Contains("min_app_version", keys);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/settings/policy/offline_grace_hours", new { value = "100", reason = "x-test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/settings/policy/offline_grace_hours", new { value = "0", reason = "x-test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/settings/policy/offline_grace_hours", new { value = "12", reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/settings/policy/min_app_version", new { value = "latest", reason = "x-test" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/v1/settings/policy/no_such_policy", new { value = "1", reason = "x-test" })).StatusCode);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PostAsJsonAsync("/api/v1/settings/policy/offline_grace_hours", new { value = "12", reason = "x" })).StatusCode);

        var before = await db.ScalarAsOwnerAsync<long>("SELECT count(*) FROM audit.audit_events WHERE action = 'policy_setting.changed'");
        await SetPolicyAsync("app_lock_minutes", "7");
        await SetPolicyAsync("app_lock_minutes", "5");
        Assert.Equal(before + 2, await db.ScalarAsOwnerAsync<long>("SELECT count(*) FROM audit.audit_events WHERE action = 'policy_setting.changed'"));
    }

    // ---- offline grants ----

    [Fact]
    public async Task A_grant_is_signed_by_the_server_key_and_its_duration_comes_from_the_policy_not_the_code()
    {
        var (user, device, _, _) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        var grant = await GrantAsync(client);
        using (var key = ECDsa.Create())
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(grant.GetProperty("publicKey").GetString()!), out _);
            var payload = Encoding.UTF8.GetBytes(grant.GetProperty("signedPayload").GetString()!);
            var signature = Convert.FromBase64String(grant.GetProperty("signature").GetString()!);
            Assert.True(key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
            payload[10] ^= 1;
            Assert.False(key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        }

        var signed = JsonDocument.Parse(grant.GetProperty("signedPayload").GetString()!).RootElement;
        Assert.Equal(device, signed.GetProperty("device_id").GetGuid());
        Assert.Equal(Guid.Parse(user), signed.GetProperty("user_id").GetGuid());
        Assert.Equal(24, grant.GetProperty("policyHours").GetInt32());
        Assert.Equal(TimeSpan.FromHours(24), grant.GetProperty("expiresAt").GetDateTimeOffset() - grant.GetProperty("issuedAt").GetDateTimeOffset());
        Assert.Equal(5, grant.GetProperty("policy").GetProperty("appLockMinutes").GetInt32());

        try
        {
            await SetPolicyAsync("offline_grace_hours", "6");
            var shorter = await GrantAsync(client);
            Assert.Equal(TimeSpan.FromHours(6), shorter.GetProperty("expiresAt").GetDateTimeOffset() - shorter.GetProperty("issuedAt").GetDateTimeOffset());
            Assert.Equal(6, shorter.GetProperty("policy").GetProperty("offlineGraceHours").GetInt32());

            // The newer grant replaced the older one (revoked NEW_GRANT, audited).
            Assert.Equal(1, await db.ScalarAsOwnerAsync<long>(
                $"SELECT count(*) FROM access.offline_grant_revocations WHERE grant_id = '{grant.GetProperty("grantId").GetGuid()}' AND reason = 'NEW_GRANT'"));
        }
        finally
        {
            await SetPolicyAsync("offline_grace_hours", "24");
        }

        // Only an ACTIVE device gets a grant, and the request must be signed.
        var pendingUser = await App.CreateUserAsync("TECHNICIAN");
        var pendingDevice = await App.RegisterDeviceAsync(pendingUser);
        using var pending = App.FieldAppClientFor(pendingUser, pendingDevice);
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.PostAsJsonAsync("/sync/v1/grant", new { })).StatusCode);
    }

    [Fact]
    public async Task Every_command_must_carry_a_grant_of_this_device()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        phone.AutoGrant = false;
        var missing = await PushAsync(client, Fault(assetId));
        Assert.Equal(("REJECTED", TrustErrorCodes.GrantRequired), (missing.GetProperty("result").GetString(), missing.GetProperty("code").GetString()));

        // Another device's grant: rejected and flagged for the auditor (a normal app cannot do this).
        var (otherUser, otherDevice, _, _) = await PhoneAsync();
        var foreign = (await GrantAsync(App.FieldAppClientFor(otherUser, otherDevice))).GetProperty("grantId").GetGuid();
        var c = Fault(assetId);
        c["grantId"] = foreign.ToString("D");
        var invalid = await PushAsync(client, c);
        Assert.Equal(TrustErrorCodes.GrantInvalid, invalid.GetProperty("code").GetString());
        Assert.Equal(1, await FlagsAsync("GRANT_INVALID", device));
        Assert.Equal(0, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));
    }

    [Fact]
    public async Task Work_done_offline_inside_the_grant_is_accepted_when_the_phone_reconnects_a_day_later()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        phone.GrantId = (await GrantAsync(client)).GetProperty("grantId").GetGuid();

        // Offline: two hours later a fault is recorded on the phone (no request reaches the server).
        _clock.Offset = TimeSpan.FromHours(2);
        phone.ElapsedOffset = TimeSpan.FromHours(2);
        var made = Offline(phone, assetId);

        // 30 hours after the grant (past its 24 h), the phone is back online on the same boot and sends its outbox.
        _clock.Offset = TimeSpan.FromHours(30);
        phone.ElapsedOffset = TimeSpan.FromHours(30);
        var result = await PushAsync(client, made);
        Assert.Equal("APPLIED", result.GetProperty("result").GetString());
        var receipt = await db.ScalarAsOwnerAsync<string>($"SELECT time_confidence FROM sync.sync_commands WHERE command_id = '{made["commandId"]}'");
        Assert.Equal("HIGH", receipt);

        // Placed on the server's clock at creation (≈ +2 h), not at receipt (+30 h).
        var estimated = await db.ScalarAsOwnerAsync<DateTime>($"SELECT estimated_time FROM sync.sync_commands WHERE command_id = '{made["commandId"]}'");
        var expected = DateTime.UtcNow.AddHours(2);
        Assert.InRange(estimated, expected.AddMinutes(-2), expected.AddMinutes(2));
    }

    [Fact]
    public async Task A_command_made_after_the_grant_expired_is_rejected_and_flagged()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        phone.GrantId = (await GrantAsync(client)).GetProperty("grantId").GetGuid();

        // 25 hours later, still on the same boot and the old grant (a normal app would be locked by now).
        _clock.Offset = TimeSpan.FromHours(25);
        phone.ElapsedOffset = TimeSpan.FromHours(25);
        var result = await PushAsync(client, Offline(phone, assetId));
        Assert.Equal(("REJECTED", TrustErrorCodes.GrantExpired), (result.GetProperty("result").GetString(), result.GetProperty("code").GetString()));
        Assert.Equal(1, await FlagsAsync("GRANT_EXPIRED", device));
        Assert.Equal(0, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));
    }

    [Fact]
    public async Task A_command_made_after_its_grant_was_revoked_is_rejected()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        var first = (await GrantAsync(client)).GetProperty("grantId").GetGuid();
        var beforeRevocation = Offline(phone, assetId, "FLICKERING");
        beforeRevocation["grantId"] = first.ToString("D");

        _clock.Offset = TimeSpan.FromHours(1);
        phone.ElapsedOffset = TimeSpan.FromHours(1);
        phone.GrantId = (await GrantAsync(client)).GetProperty("grantId").GetGuid(); // revokes the first grant (NEW_GRANT)

        _clock.Offset = TimeSpan.FromHours(2);
        phone.ElapsedOffset = TimeSpan.FromHours(2);
        var afterRevocation = Offline(phone, assetId, "DAY_BURNING");
        afterRevocation["grantId"] = first.ToString("D");

        var ok = await PushAsync(client, beforeRevocation);
        Assert.Equal("APPLIED", ok.GetProperty("result").GetString());
        var revoked = await PushAsync(client, afterRevocation);
        Assert.Equal(("REJECTED", TrustErrorCodes.GrantRevoked), (revoked.GetProperty("result").GetString(), revoked.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task When_the_time_cannot_be_proven_the_command_is_quarantined_and_a_supervisor_decides()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        phone.GrantId = (await GrantAsync(client)).GetProperty("grantId").GetGuid();

        // Offline, the phone was restarted, the work was done, and it was restarted again before it reconnected after the grant:
        // the server has no sample for the boot the command was made on (LOW confidence).
        phone.Reboot();
        var unproven = Offline(phone, assetId);
        phone.Reboot();
        _clock.Offset = TimeSpan.FromHours(26);
        var held = await PushAsync(client, unproven);
        Assert.Equal(("QUARANTINED", TrustErrorCodes.GrantWindowUnproven), (held.GetProperty("result").GetString(), held.GetProperty("code").GetString()));
        Assert.Equal(0, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));

        // A resend reports the same state (nothing applied twice).
        Assert.Equal("QUARANTINED", (await PushAsync(client, unproven)).GetProperty("result").GetString());

        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var queue = await supervisor.GetFromJsonAsync<JsonElement>("/api/v1/sync/quarantine?state=PENDING");
        var entry = queue.EnumerateArray().Single(q => q.GetProperty("commandId").GetString() == unproven["commandId"]!.GetValue<string>());
        Assert.Equal("LOW", entry.GetProperty("timeConfidence").GetString());
        var id = entry.GetProperty("id").GetGuid();

        // The author cannot decide on their own command; a reason is required.
        using var author = App.ClientFor(user);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsJsonAsync($"/api/v1/sync/quarantine/{id}/resolve", new { decision = "APPLY", reason = "لي" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await supervisor.PostAsJsonAsync($"/api/v1/sync/quarantine/{id}/resolve", new { decision = "APPLY", reason = "" })).StatusCode);

        var applied = await supervisor.PostAsJsonAsync($"/api/v1/sync/quarantine/{id}/resolve", new { decision = "APPLY", reason = "تأكدت من الفني هاتفيًا" });
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(1, await db.ScalarAsOwnerAsync<long>($"SELECT count(*) FROM faults.faults WHERE asset_id = '{assetId}'"));

        // The fault is the technician's (with their device); the decision is the supervisor's.
        Assert.Equal(1, await db.ScalarAsOwnerAsync<long>(
            $"SELECT count(*) FROM audit.audit_events WHERE action = 'fault.reported' AND command_id = '{unproven["commandId"]}' AND actor_user_id = '{user}' AND device_id = '{device}'"));
        Assert.Equal(1, await db.ScalarAsOwnerAsync<long>(
            $"SELECT count(*) FROM audit.audit_events WHERE action = 'sync_quarantine.applied' AND actor_user_id = '{DevUsers.Supervisor}'"
            + $" AND entity_id = '{id}'"));
        Assert.Equal(HttpStatusCode.Conflict, (await supervisor.PostAsJsonAsync($"/api/v1/sync/quarantine/{id}/resolve", new { decision = "DISCARD", reason = "مرة ثانية" })).StatusCode);
        Assert.Equal("DUPLICATE", (await PushAsync(client, unproven)).GetProperty("result").GetString());

        // Discard: recorded, nothing applied, and the phone learns it on resend.
        phone.Reboot();
        var second = Offline(phone, assetId, "DAY_BURNING");
        phone.Reboot();
        _clock.Offset = TimeSpan.FromHours(52);
        Assert.Equal("QUARANTINED", (await PushAsync(client, second)).GetProperty("result").GetString());
        var secondId = (await supervisor.GetFromJsonAsync<JsonElement>("/api/v1/sync/quarantine?state=PENDING")).EnumerateArray()
            .Single(q => q.GetProperty("commandId").GetString() == second["commandId"]!.GetValue<string>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await supervisor.PostAsJsonAsync($"/api/v1/sync/quarantine/{secondId}/resolve", new { decision = "DISCARD", reason = "مكرر" })).StatusCode);
        var discarded = await PushAsync(client, second);
        Assert.Equal(("REJECTED", "QUARANTINE_DISCARDED"), (discarded.GetProperty("result").GetString(), discarded.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task A_phone_clock_set_back_does_not_move_the_command_in_time_and_raises_one_clock_skew_flag()
    {
        var (user, device, phone, assetId) = await PhoneAsync();
        using var client = App.FieldAppClientFor(user, device);
        var c1 = Fault(assetId, "FLICKERING");
        var c2 = Fault(assetId, "DAY_BURNING");
        c1["deviceTime"] = DateTimeOffset.UtcNow.AddDays(-3).ToString("O");
        c2["deviceTime"] = DateTimeOffset.UtcNow.AddDays(-3).ToString("O");
        var response = await client.PostAsJsonAsync("/sync/v1/push", new { commands = new[] { c1, c2 }, pendingCommands = 0 });
        var results = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results").EnumerateArray().ToList();
        Assert.All(results, r => Assert.Equal("APPLIED", r.GetProperty("result").GetString()));
        Assert.Equal(1, await FlagsAsync("CLOCK_SKEW", device));
        var estimated = await db.ScalarAsOwnerAsync<DateTime>($"SELECT estimated_time FROM sync.sync_commands WHERE command_id = '{c1["commandId"]}'");
        Assert.InRange(estimated, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
    }
}
