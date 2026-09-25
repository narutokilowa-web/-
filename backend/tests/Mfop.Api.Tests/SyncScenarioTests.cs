using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Access.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Api.Tests;

/// <summary>
/// M4 sync scenario suite (docs/lean-pilot/10 "Sync scenarios", docs/spec/10 §10.5): work done offline meets a server that moved
/// on — reassignment, cancellation, a stale view, a revoked phone, an offboarded employee, a dropped upload, a lost response.
/// Each scenario checks: no field evidence lost, no newer server decision overwritten, the expected final state, the audit trail.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class SyncScenarioTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly ShiftableClock _clock = new();

    private TestApp App => _app ??= new TestApp(db, clock: _clock);

    private TestApp? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    private static JsonObject Command(string type, object payload, int? baseVersion = null)
    {
        var c = new JsonObject
        {
            ["commandId"] = Guid.CreateVersion7().ToString("D"),
            ["type"] = type,
            ["deviceTime"] = DateTimeOffset.UtcNow.ToString("O"),
            ["payload"] = JsonSerializer.SerializeToNode(payload),
        };
        if (baseVersion is { } v) c["baseVersion"] = v;
        return c;
    }

    private static async Task<List<JsonElement>> PushAsync(HttpClient phone, params JsonObject[] commands)
    {
        var response = await phone.PostAsJsonAsync("/sync/v1/push", new { commands, pendingCommands = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results").EnumerateArray().ToList();
    }

    private static (string Result, string? Code) Outcome(JsonElement r) =>
        (r.GetProperty("result").GetString()!, r.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null);

    private static async Task<int> VersionAsync(HttpClient phone, Guid workOrderId) =>
        (await phone.GetFromJsonAsync<JsonElement>($"/sync/v1/work-orders/{workOrderId}")).GetProperty("eventSeq").GetInt32();

    private async Task<(string User, Guid Device, HttpClient Phone)> TechnicianWithPhoneAsync()
    {
        var user = await App.CreateUserAsync("TECHNICIAN");
        var device = await App.ActiveDeviceAsync(user);
        TestPhone.For(device)!.SignatureClock = _clock.GetUtcNow;
        return (user, device, App.FieldAppClientFor(user, device));
    }

    private async Task<FieldFlow> FlowAsync()
    {
        var flow = await FieldFlow.StartAsync(App);
        TestPhone.For(flow.DeviceId)!.SignatureClock = _clock.GetUtcNow;
        return flow;
    }

    private Task<string?> WorkOrderFieldAsync(Guid workOrderId, string column) =>
        db.ScalarAsOwnerAsync<object>($"SELECT {column} FROM workorders.work_orders WHERE id = '{workOrderId}'")
            .ContinueWith(t => t.Result is DBNull ? null : t.Result?.ToString());

    private Task<long> CountAsync(string sql) => db.ScalarAsOwnerAsync<long>(sql);

    private async Task ReassignAsync(Guid workOrderId, string to, string reason = "إعادة توزيع")
    {
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{workOrderId}/reassign", new { assigneeId = to, reason })).EnsureSuccessStatusCode();
    }

    private async Task RunIdpTasksAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdpTaskProcessor>().RunOnceAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Reassigned_while_offline_field_facts_are_kept_and_flagged_but_the_new_assignees_work_is_never_overwritten()
    {
        using var flow = await FlowAsync();
        await flow.ArriveAsync();
        var seen = await VersionAsync(flow.Phone, flow.WorkOrderId);
        var photo = TestJpeg.Create();
        var photoId = Guid.CreateVersion7();

        // Offline on A's phone: a before photo, a diagnosis, a part used — all on the version A saw.
        var outbox = new[]
        {
            Command("AddEvidence", new { workOrderId = flow.WorkOrderId, evidenceId = photoId, kind = "BEFORE", sha256 = TestJpeg.Sha256(photo), sizeBytes = photo.LongLength, mime = "image/jpeg" }, seen),
            Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL", note = "من هاتف أ" }, seen),
            Command("ConsumeMaterial", new { workOrderId = flow.WorkOrderId, movementId = Guid.CreateVersion7(), itemId = flow.ItemId, quantity = 1, serials = new[] { flow.Serials[0] } }, seen),
        };

        // Meanwhile the supervisor reassigns the work order to B, who arrives and records a different diagnosis online.
        var (b, _, bPhone) = await TechnicianWithPhoneAsync();
        await ReassignAsync(flow.WorkOrderId, b);
        Assert.Equal("APPLIED", Outcome((await PushAsync(bPhone, Command("ArriveOnSite", new { workOrderId = flow.WorkOrderId, scannedToken = flow.TagToken })))[0]).Result);
        Assert.Equal("APPLIED", Outcome((await PushAsync(bPhone, Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL", note = "من هاتف ب" })))[0]).Result);

        // A reconnects.
        var results = await PushAsync(flow.Phone, outbox);
        Assert.Equal(("APPLIED", (string?)null), Outcome(results[0]));
        Assert.Equal(("REJECTED", "WORK_ORDER_REASSIGNED"), Outcome(results[1]));
        Assert.Equal("APPLIED", Outcome(results[2]).Result);

        // B's diagnosis stands; A's photo and part are linked to the work order and flagged for the supervisor.
        Assert.Equal("LED_MODULE_FAIL", await WorkOrderFieldAsync(flow.WorkOrderId, "diagnosis_code"));
        Assert.Equal(HttpStatusCode.OK, (await flow.UploadAsync(photoId, photo)).StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM evidence.evidence WHERE id = '{photoId}' AND owner_id = '{flow.WorkOrderId}'"));
        Assert.Equal(2, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'SUBMITTED_BY_NON_ASSIGNEE' AND subject_id = '{flow.WorkOrderId}'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.stock_movements WHERE work_order_id = '{flow.WorkOrderId}' AND actor_id = '{flow.Technician}'"));

        // A stranger to the work order is still refused outright.
        var (stranger, _, strangerPhone) = await TechnicianWithPhoneAsync();
        Assert.Equal(("REJECTED", "NOT_ASSIGNEE"), Outcome((await PushAsync(strangerPhone,
            Command("AddEvidence", new { workOrderId = flow.WorkOrderId, evidenceId = Guid.CreateVersion7(), kind = "BEFORE", sha256 = new string('a', 64), sizeBytes = 10, mime = "image/jpeg" })))[0]));
        Assert.NotEqual(stranger, b);
    }

    [Fact]
    public async Task A_change_made_on_a_stale_view_is_refused_until_the_phone_refreshes()
    {
        using var flow = await FlowAsync();
        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        var seen = await VersionAsync(flow.Phone, flow.WorkOrderId);

        // The supervisor moves the work order away and back (reason recorded each time) while A is offline.
        var (cover, _, _) = await TechnicianWithPhoneAsync();
        await ReassignAsync(flow.WorkOrderId, cover, "تغطية مؤقتة");
        await ReassignAsync(flow.WorkOrderId, flow.Technician, "عاد الفني");

        var stale = await PushAsync(flow.Phone, Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL", note = "قديم" }, seen));
        Assert.Equal(("REJECTED", "WORK_ORDER_CHANGED"), Outcome(stale[0]));
        Assert.Null(await WorkOrderFieldAsync(flow.WorkOrderId, "diagnosis_code"));
        Assert.Equal("ASSIGNED", await WorkOrderFieldAsync(flow.WorkOrderId, "state"));

        // A missing version on a change is refused too (a phone must say what it saw).
        var phone = TestPhone.For(flow.DeviceId)!;
        phone.AutoBaseVersion = false;
        Assert.Equal(("REJECTED", "BASE_VERSION_REQUIRED"), Outcome((await PushAsync(flow.Phone, Command("ArriveOnSite", new { workOrderId = flow.WorkOrderId, scannedToken = flow.TagToken })))[0]));

        // After refreshing, the technician repeats the step on the current version.
        phone.AutoBaseVersion = true;
        await flow.ArriveAsync();
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL", note = "بعد التحديث" });
        Assert.Equal("DRIVER_FAIL", await WorkOrderFieldAsync(flow.WorkOrderId, "diagnosis_code"));

        // The technician's own earlier commands in the same batch never count as a conflict.
        var own = await VersionAsync(flow.Phone, flow.WorkOrderId);
        var batch = await PushAsync(flow.Phone,
            Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL", note = "1" }, own),
            Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL", note = "2" }, own));
        Assert.All(batch, r => Assert.Equal("APPLIED", Outcome(r).Result));
    }

    [Fact]
    public async Task Work_on_a_work_order_cancelled_while_offline_is_quarantined_for_the_supervisor()
    {
        using var flow = await FlowAsync();
        var seen = await VersionAsync(flow.Phone, flow.WorkOrderId);
        var diagnosis = Command("ArriveOnSite", new { workOrderId = flow.WorkOrderId, scannedToken = flow.TagToken }, seen);
        var photo = Command("AddEvidence", new { workOrderId = flow.WorkOrderId, evidenceId = Guid.CreateVersion7(), kind = "BEFORE", sha256 = new string('c', 64), sizeBytes = 100, mime = "image/jpeg" }, seen);

        using (var supervisor = App.ClientFor(DevUsers.Supervisor))
        {
            (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/cancel", new { reason = "بلاغ مكرر" })).EnsureSuccessStatusCode();
        }

        var results = await PushAsync(flow.Phone, diagnosis, photo);
        Assert.All(results, r => Assert.Equal(("QUARANTINED", "WORK_ORDER_CANCELLED"), Outcome(r)));
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM workorders.wo_arrivals WHERE work_order_id = '{flow.WorkOrderId}'"));
        Assert.Equal("CANCELLED", await WorkOrderFieldAsync(flow.WorkOrderId, "state"));

        using var sup = App.ClientFor(DevUsers.Supervisor);
        var queue = (await sup.GetFromJsonAsync<JsonElement>("/api/v1/sync/quarantine?state=PENDING")).EnumerateArray()
            .Where(q => q.GetProperty("userId").GetString() == flow.Technician).ToList();
        Assert.Equal(2, queue.Count);

        // Applying cannot bring back a cancelled work order: recorded as APPLY_FAILED. Discarding is recorded with its reason.
        var apply = await sup.PostAsJsonAsync($"/api/v1/sync/quarantine/{queue[0].GetProperty("id").GetGuid()}/resolve", new { decision = "APPLY", reason = "محاولة" });
        Assert.Equal(HttpStatusCode.Conflict, apply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sup.PostAsJsonAsync($"/api/v1/sync/quarantine/{queue[1].GetProperty("id").GetGuid()}/resolve", new { decision = "DISCARD", reason = "الأمر ملغى" })).StatusCode);
        var states = (await sup.GetFromJsonAsync<JsonElement>("/api/v1/sync/quarantine")).EnumerateArray()
            .Where(q => q.GetProperty("userId").GetString() == flow.Technician).Select(q => q.GetProperty("state").GetString()).Order().ToList();
        Assert.Equal(["APPLY_FAILED", "DISCARDED"], states);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM audit.audit_events WHERE action = 'sync_quarantine.apply_failed' AND actor_user_id = '{DevUsers.Supervisor}' AND entity_id = '{queue[0].GetProperty("id").GetGuid()}'"));
    }

    [Fact]
    public async Task A_revoked_phone_cannot_send_anything_its_grants_end_and_its_app_sessions_are_logged_out()
    {
        using var flow = await FlowAsync();
        await flow.ArriveAsync();
        var queued = Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL", note = "بعد الإلغاء" }, await VersionAsync(flow.Phone, flow.WorkOrderId));

        using (var supervisor = App.ClientFor(DevUsers.Supervisor))
        {
            (await supervisor.PostAsJsonAsync($"/api/v1/devices/{flow.DeviceId}/revoke", new { reason = "هاتف مفقود" })).EnsureSuccessStatusCode();
        }

        var push = await flow.Phone.PostAsJsonAsync("/sync/v1/push", new { commands = new[] { queued }, pendingCommands = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
        Assert.Equal("DEVICE_REVOKED", await push.ProblemCodeAsync());
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM sync.sync_commands WHERE command_id = '{queued["commandId"]}'"));
        Assert.Null(await WorkOrderFieldAsync(flow.WorkOrderId, "diagnosis_code"));
        foreach (var url in new[] { "/sync/v1/pull", $"/sync/v1/work-orders/{flow.WorkOrderId}" })
        {
            Assert.Equal("DEVICE_REVOKED", await (await flow.Phone.GetAsync(url)).ProblemCodeAsync());
        }

        Assert.Equal("DEVICE_REVOKED", await (await flow.Phone.PostAsJsonAsync("/sync/v1/grant", new { })).ProblemCodeAsync());
        Assert.Equal("DEVICE_REVOKED", await (await flow.UploadAsync(Guid.CreateVersion7(), TestJpeg.Create())).ProblemCodeAsync());

        // Its grants are revoked (server-side) and the field-app sessions are ended in Keycloak after commit.
        Assert.True(await CountAsync($"SELECT count(*) FROM access.offline_grant_revocations r JOIN access.offline_grants g ON g.id = r.grant_id WHERE g.device_id = '{flow.DeviceId}' AND r.reason = 'DEVICE_REVOKED'") >= 1);
        await RunIdpTasksAsync();
        Assert.Contains(("LOGOUT_MOBILE", flow.Technician), App.Services.GetRequiredService<FakeIdentityProvider>().Calls);
    }

    [Fact]
    public async Task Offboarding_disables_the_user_everywhere_in_one_audited_step_and_queues_their_open_work_for_reassignment()
    {
        using var flow = await FlowAsync();
        await flow.ArriveAsync();
        var pendingDevice = await App.RegisterDeviceAsync(flow.Technician, "Replacement phone");
        var fake = App.Services.GetRequiredService<FakeIdentityProvider>();

        // A command whose time cannot be proven waits in quarantine (see OfflineTrustTests) — made before the user leaves.
        var phone = TestPhone.For(flow.DeviceId)!;
        var seen = await VersionAsync(flow.Phone, flow.WorkOrderId);
        phone.Reboot();
        var unproven = Command("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL", note = "x" }, seen);
        unproven["elapsedMs"] = phone.ElapsedMs;
        unproven["bootId"] = phone.BootId;
        phone.Reboot();
        _clock.Offset = TimeSpan.FromHours(26);
        Assert.Equal("QUARANTINED", Outcome((await PushAsync(flow.Phone, unproven))[0]).Result);
        Assert.Equal(HttpStatusCode.OK, (await flow.Phone.PostAsJsonAsync("/sync/v1/grant", new { })).StatusCode); // back online: a live grant

        using var admin = App.ClientFor(DevUsers.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/v1/users/{flow.Technician}/disable", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/users/{DevUsers.Admin}/disable", new { reason = "نفسي" })).StatusCode);
        using (var supervisor = App.ClientFor(DevUsers.Supervisor))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PostAsJsonAsync($"/api/v1/users/{flow.Technician}/disable", new { reason = "x" })).StatusCode);
        }

        var commandId = Guid.NewGuid();
        using (var adminCmd = App.ClientFor(DevUsers.Admin, commandId))
        {
            Assert.Equal(HttpStatusCode.OK, (await adminCmd.PostAsJsonAsync($"/api/v1/users/{flow.Technician}/disable", new { reason = "انتهاء العقد" })).StatusCode);
        }

        // One transaction, one command id: user, devices, grants, flags.
        var actions = await db.AuditActionsForCommandAsync(commandId);
        Assert.Equal("user.disabled", actions[0]);
        Assert.Contains("device.revoked", actions);
        Assert.Contains("device.rejected", actions);
        Assert.Contains("offline_grant.revoked", actions);
        Assert.Equal(2, actions.Count(a => a == "flag.raised"));

        Assert.Equal("REVOKED", await db.ScalarAsOwnerAsync<string>($"SELECT status FROM access.devices WHERE id = '{flow.DeviceId}'"));
        Assert.Equal("USER_DISABLED", await db.ScalarAsOwnerAsync<string>($"SELECT revoke_reason FROM access.devices WHERE id = '{flow.DeviceId}'"));
        Assert.Equal("REJECTED", await db.ScalarAsOwnerAsync<string>($"SELECT status FROM access.devices WHERE id = '{pendingDevice}'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'NEEDS_REASSIGNMENT' AND subject_id = '{flow.WorkOrderId}'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CUSTODY_TO_RECONCILE' AND subject_id = '{flow.Technician}' AND severity = 'INFO'"));

        // Nothing more is accepted from them.
        var after = await flow.Phone.PostAsJsonAsync("/sync/v1/push", new { commands = new[] { Command("StartWork", new { workOrderId = flow.WorkOrderId }) }, pendingCommands = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        Assert.Equal("USER_DISABLED", await after.ProblemCodeAsync());

        // Their quarantined command can only be discarded now.
        using var sup = App.ClientFor(DevUsers.Supervisor);
        var entry = (await sup.GetFromJsonAsync<JsonElement>("/api/v1/sync/quarantine?state=PENDING")).EnumerateArray()
            .Single(q => q.GetProperty("commandId").GetString() == unproven["commandId"]!.GetValue<string>()).GetProperty("id").GetGuid();
        var apply = await sup.PostAsJsonAsync($"/api/v1/sync/quarantine/{entry}/resolve", new { decision = "APPLY", reason = "x" });
        Assert.Equal("ACTOR_NOT_ACTIVE", await apply.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await sup.PostAsJsonAsync($"/api/v1/sync/quarantine/{entry}/resolve", new { decision = "DISCARD", reason = "غادر الموظف" })).StatusCode);

        // The supervisor's queue shows the open work order until it is reassigned.
        var queue = await sup.GetFromJsonAsync<JsonElement>("/api/v1/work-orders/needs-reassignment");
        Assert.Contains(queue.EnumerateArray(), w => w.GetProperty("id").GetGuid() == flow.WorkOrderId);
        var (successor, _, _) = await TechnicianWithPhoneAsync();
        await ReassignAsync(flow.WorkOrderId, successor, "الفني السابق غادر");
        queue = await sup.GetFromJsonAsync<JsonElement>("/api/v1/work-orders/needs-reassignment");
        Assert.DoesNotContain(queue.EnumerateArray(), w => w.GetProperty("id").GetGuid() == flow.WorkOrderId);

        // Keycloak is updated after commit, with retry; repeated failures raise an alert on the Users page.
        fake.Fail = true;
        for (var i = 0; i < 5; i++)
        {
            _clock.Offset += TimeSpan.FromHours(2);
            await RunIdpTasksAsync();
        }

        var listed = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("id").GetString() == flow.Technician);
        Assert.Equal("DISABLED", listed.GetProperty("status").GetString());
        Assert.True(listed.GetProperty("idpAlert").GetBoolean());
        Assert.Equal(2, listed.GetProperty("idpPending").GetInt32());

        fake.Fail = false;
        _clock.Offset += TimeSpan.FromHours(2);
        await RunIdpTasksAsync();
        Assert.Contains(("DISABLE_USER", flow.Technician), fake.Calls);
        Assert.Contains(("LOGOUT_ALL", flow.Technician), fake.Calls);
        listed = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("id").GetString() == flow.Technician);
        Assert.Equal(0, listed.GetProperty("idpPending").GetInt32());

        // Enabling again (with a reason) restores the account, not the devices: a new phone must be approved.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/users/{flow.Technician}/enable", new { reason = "عاد إلى العمل" })).StatusCode);
        Assert.Equal("REVOKED", await db.ScalarAsOwnerAsync<string>($"SELECT status FROM access.devices WHERE id = '{flow.DeviceId}'"));
        Assert.Equal("DEVICE_REVOKED", await (await flow.Phone.GetAsync("/sync/v1/pull")).ProblemCodeAsync());
        await RunIdpTasksAsync();
        Assert.Contains(("ENABLE_USER", flow.Technician), fake.Calls);
    }

    [Fact]
    public async Task A_photo_upload_interrupted_on_a_weak_network_resumes_from_where_the_server_stopped()
    {
        using var flow = await FlowAsync();
        await flow.ArriveAsync();
        var bytes = TestJpeg.Create(400, 300);
        var id = await flow.PhotoAsync("BEFORE", bytes, upload: false);
        var half = bytes.Length / 2;

        async Task<HttpResponseMessage> Chunk(int from, int to)
        {
            var content = new ByteArrayContent(bytes[from..to]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to - 1, bytes.Length);
            return await flow.Phone.PutAsync($"/sync/v1/evidence/{id}/content", content);
        }

        var first = await Chunk(0, half);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(half, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receivedBytes").GetInt64());

        // The connection dropped; the phone asks where to continue.
        var status = await flow.Phone.GetFromJsonAsync<JsonElement>($"/sync/v1/evidence/{id}/upload");
        Assert.Equal(half, status.GetProperty("receivedBytes").GetInt64());
        Assert.Equal("ANNOUNCED", status.GetProperty("state").GetString());

        // A chunk that does not start where the server stopped is refused with the offset to resume from.
        var wrong = await Chunk(half + 10, bytes.Length);
        Assert.Equal(HttpStatusCode.Conflict, wrong.StatusCode);
        var problem = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UPLOAD_OFFSET_MISMATCH", problem.GetProperty("code").GetString());
        Assert.Equal(half.ToString(System.Globalization.CultureInfo.InvariantCulture), problem.GetProperty("details").GetProperty("receivedBytes").GetString());

        var last = await Chunk(half, bytes.Length);
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        var done = await last.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UPLOADED", done.GetProperty("state").GetString());
        Assert.Equal(TestJpeg.Sha256(bytes), done.GetProperty("sha256Server").GetString());

        // Another technician cannot continue someone else's upload; verification proceeds as for a single upload.
        var (_, _, other) = await TechnicianWithPhoneAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/sync/v1/evidence/{id}/upload")).StatusCode);
        var review = await flow.WaitVerifiedAsync();
        Assert.Contains(review.GetProperty("evidence").EnumerateArray(), e => e.GetProperty("id").GetGuid() == id && e.GetProperty("state").GetString() == "VERIFIED");
    }

    [Fact]
    public async Task After_reconnecting_a_batch_whose_response_was_lost_is_sent_again_and_nothing_is_applied_twice()
    {
        using var flow = await FlowAsync();
        var seen = await VersionAsync(flow.Phone, flow.WorkOrderId);
        var arrive = Command("ArriveOnSite", new { workOrderId = flow.WorkOrderId, scannedToken = flow.TagToken }, seen);
        var photo = TestJpeg.Create();
        var photoCommand = Command("AddEvidence", new { workOrderId = flow.WorkOrderId, evidenceId = Guid.CreateVersion7(), kind = "BEFORE", sha256 = TestJpeg.Sha256(photo), sizeBytes = photo.LongLength, mime = "image/jpeg" }, seen);
        var first = await PushAsync(flow.Phone, arrive, photoCommand);
        Assert.All(first, r => Assert.Equal("APPLIED", Outcome(r).Result));

        // The phone never saw that response; it resends the whole outbox plus what it recorded since.
        var photo2 = TestJpeg.Create();
        var second = Command("AddEvidence", new { workOrderId = flow.WorkOrderId, evidenceId = Guid.CreateVersion7(), kind = "BEFORE", sha256 = TestJpeg.Sha256(photo2), sizeBytes = photo2.LongLength, mime = "image/jpeg" }, seen);
        var again = await PushAsync(flow.Phone, arrive, photoCommand, second);
        Assert.Equal(["DUPLICATE", "DUPLICATE", "APPLIED"], again.Select(r => Outcome(r).Result));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM workorders.wo_arrivals WHERE work_order_id = '{flow.WorkOrderId}'"));
        Assert.Equal(2, await CountAsync($"SELECT count(*) FROM evidence.evidence WHERE owner_id = '{flow.WorkOrderId}'"));
    }
}
