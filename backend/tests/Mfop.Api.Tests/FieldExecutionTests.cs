using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mfop.AuditVerify;
using Mfop.Evidence.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Api.Tests;

/// <summary>M3: technician flow on the phone, evidence pipeline, material, checklist, review, closure and seal (DEMO 1 steps 10–17).</summary>
[Collection(nameof(DatabaseCollection))]
public class FieldExecutionTests(DatabaseFixture db) : IAsyncLifetime
{
    private TestApp App { get; } = new(db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    [Fact]
    public async Task Full_flow_closes_seals_resolves_the_fault_and_makes_the_light_operational()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.WorkUntilReadyToSubmitAsync();

        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var asset = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{flow.AssetId}");
        Assert.Equal("UNDER_MAINTENANCE", asset.GetProperty("operationalStatus").GetString()); // OS5 on StartWork

        await flow.ApplyAsync("SubmitWorkOrder", new { workOrderId = flow.WorkOrderId, rootCauseNote = "احتراق وحدة الليد" });
        var review = await flow.WaitVerifiedAsync();
        Assert.Equal("PENDING_REVIEW", review.GetProperty("state").GetString());
        Assert.True(review.GetProperty("canApprove").GetBoolean());
        Assert.Equal(5, review.GetProperty("evidence").EnumerateArray().Count(e => e.GetProperty("state").GetString() == "VERIFIED"));
        Assert.True(review.GetProperty("evidence").EnumerateArray().All(e => e.GetProperty("hasPreview").GetBoolean()));
        var material = review.GetProperty("materials")[0];
        Assert.Equal("CONSUME", material.GetProperty("type").GetString());
        Assert.Equal("SYP", material.GetProperty("totalCost").GetProperty("currency").GetString());
        Assert.Equal(185000m, material.GetProperty("totalCost").GetProperty("amount").GetDecimal());
        Assert.Equal("PASS", review.GetProperty("checklist").GetProperty("testResult").GetString());
        Assert.Single(review.GetProperty("submissions").EnumerateArray());

        var approve = await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = "عمل جيد" });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var closed = await flow.ReviewAsync();
        Assert.Equal("CLOSED", closed.GetProperty("state").GetString());
        var seal = closed.GetProperty("sealHash").GetString();
        Assert.Matches("^[0-9a-f]{64}$", seal);
        Assert.Equal("RESOLVED", (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{flow.FaultId}")).GetProperty("state").GetString());
        Assert.Equal("OPERATIONAL", (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{flow.AssetId}")).GetProperty("operationalStatus").GetString());
        Assert.DoesNotContain((await flow.Phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull")).GetProperty("workOrders").EnumerateArray(),
            t => t.GetProperty("id").GetGuid() == flow.WorkOrderId);

        // The seal is in the audit log, and the independent verifier recomputes it.
        var auditSeal = await db.ScalarAsOwnerAsync<string>(
            $"SELECT facts->>'seal_hash' FROM audit.audit_events WHERE action = 'work_order.closed' AND entity_id = '{flow.WorkOrderId}'");
        Assert.Equal(seal, auditSeal);
        var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
        Assert.True(report.Ok, string.Join("; ", report.Findings.Select(f => f.Code + " " + f.Message)));
        Assert.True(report.WorkOrdersChecked >= 1);

        // Timeline: every command with actor, device and hash.
        var timeline = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{flow.WorkOrderId}/timeline");
        var actions = timeline.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToHashSet();
        foreach (var a in new[] { "work_order.arrived_on_site", "evidence.announced", "evidence.uploaded", "evidence.verified", "work_order.diagnosis_recorded",
                     "work_order.work_started", "stock_movement.consume", "checklist_result.recorded", "work_order.submitted", "work_order.closed" })
        {
            Assert.Contains(a, actions);
        }

        var arrived = timeline.EnumerateArray().First(e => e.GetProperty("action").GetString() == "work_order.arrived_on_site");
        Assert.Equal(flow.DeviceId, arrived.GetProperty("deviceId").GetGuid());
        Assert.Matches("^[0-9a-f]{64}$", arrived.GetProperty("hash").GetString());

        // Dashboard lists it as recently completed.
        var dashboard = await supervisor.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary");
        Assert.Contains(dashboard.GetProperty("recentlyCompleted").EnumerateArray(), w => w.GetProperty("id").GetGuid() == flow.WorkOrderId);
    }

    [Fact]
    public async Task N4_submit_with_one_after_photo_is_EVIDENCE_MISSING()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL" });
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
        await flow.ConsumeAsync();
        await flow.ChecklistAsync();
        await flow.PhotoAsync("TEST");
        await flow.PhotoAsync("AFTER");
        await FieldFlow.AssertRejectedAsync(flow.SubmitAsync(), "EVIDENCE_MISSING");
        await flow.PhotoAsync("AFTER");
        Assert.Equal("APPLIED", (await flow.SubmitAsync()).GetProperty("result").GetString());
    }

    [Fact]
    public async Task Start_work_needs_the_diagnosis_and_two_before_photos_and_before_photos_need_arrival()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("AddEvidence", new
        {
            workOrderId = flow.WorkOrderId, evidenceId = Guid.CreateVersion7(), kind = "BEFORE", sha256 = new string('a', 64), sizeBytes = 10, mime = "image/jpeg",
        }), "INVALID_TRANSITION");
        await flow.ArriveAsync();
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("StartWork", new { workOrderId = flow.WorkOrderId }), "DIAGNOSIS_REQUIRED");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL" });
        await flow.PhotoAsync("BEFORE");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("StartWork", new { workOrderId = flow.WorkOrderId }), "EVIDENCE_MISSING");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
    }

    [Fact]
    public async Task Arrival_needs_this_lights_tag_or_a_typed_code_with_a_reason()
    {
        using var flow = await FieldFlow.StartAsync(App);
        var (_, otherAsset) = await App.RegisterAndApproveAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var other = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{otherAsset}");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("ArriveOnSite", new { workOrderId = flow.WorkOrderId, scannedToken = other.GetProperty("tag").GetProperty("humanCode").GetString() }), "TAG_MISMATCH");
        var code = (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{flow.AssetId}")).GetProperty("assetCode").GetString();
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("ArriveOnSite", new { workOrderId = flow.WorkOrderId, manualCode = code }), "MANUAL_CODE_REASON_REQUIRED");
        await flow.ApplyAsync("ArriveOnSite", new { workOrderId = flow.WorkOrderId, manualCode = code, manualReason = "الملصق تالف" });
        var review = await flow.ReviewAsync();
        var arrival = review.GetProperty("arrivals")[0];
        Assert.False(arrival.GetProperty("tagScanned").GetBoolean());
        Assert.Equal("الملصق تالف", arrival.GetProperty("manualReason").GetString());
    }

    [Fact]
    public async Task N2_a_supervisor_who_executed_the_work_order_cannot_approve_it()
    {
        var both = await App.CreateUserAsync("TECHNICIAN", "SUPERVISOR");
        using var flow = await FieldFlow.StartAsync(App, both);
        await flow.WorkUntilReadyToSubmitAsync();
        await flow.ApplyAsync("SubmitWorkOrder", new { workOrderId = flow.WorkOrderId, rootCauseNote = "سبب" });
        await flow.WaitVerifiedAsync();

        using var self = App.ClientFor(both);
        var response = await self.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SOD_VIOLATION", await response.ProblemCodeAsync());
        Assert.Equal("SOD_VIOLATION", (await flow.ReviewAsync(both)).GetProperty("approveBlockedReason").GetString());
        var ret = await self.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/return", new { reasons = new[] { "OTHER" }, note = "x" });
        Assert.Equal("SOD_VIOLATION", await ret.ProblemCodeAsync());
    }

    [Fact]
    public async Task N6_cancel_after_evidence_is_INVALID_TRANSITION()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.ArriveAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        // ON_SITE is not cancellable in the table either; the evidence guard is checked for ASSIGNED as well:
        var response = await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/cancel", new { reason = "no longer needed" });
        Assert.Equal("INVALID_TRANSITION", await response.ProblemCodeAsync());

        using var second = await FieldFlow.StartAsync(App);
        await second.ArriveAsync();
        await second.PhotoAsync("BEFORE");
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{second.WorkOrderId}/reassign", new { assigneeId = flow.Technician, reason = "إعادة توزيع" })).EnsureSuccessStatusCode();
        var cancel = await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{second.WorkOrderId}/cancel", new { reason = "no longer needed" });
        Assert.Equal(HttpStatusCode.Conflict, cancel.StatusCode);
        Assert.Equal("INVALID_TRANSITION", await cancel.ProblemCodeAsync()); // ASSIGNED again, but evidence exists
    }

    [Fact]
    public async Task N7_every_command_on_a_closed_work_order_is_INVALID_TRANSITION_and_N13_tampering_breaks_the_seal()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.WorkUntilReadyToSubmitAsync();
        await flow.ApplyAsync("SubmitWorkOrder", new { workOrderId = flow.WorkOrderId, rootCauseNote = "سبب" });
        await flow.WaitVerifiedAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();

        await FieldFlow.AssertRejectedAsync(flow.PushAsync("AddEvidence", new
        {
            workOrderId = flow.WorkOrderId, evidenceId = Guid.CreateVersion7(), kind = "AFTER", sha256 = new string('b', 64), sizeBytes = 10, mime = "image/jpeg",
        }), "INVALID_TRANSITION");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("ConsumeMaterial", new { workOrderId = flow.WorkOrderId, movementId = Guid.CreateVersion7(), itemId = flow.ItemId, quantity = 1, serials = new[] { flow.Serials[1] } }), "INVALID_TRANSITION");
        await FieldFlow.AssertRejectedAsync(flow.SubmitAsync("Resubmit"), "INVALID_TRANSITION");
        foreach (var (url, body) in new (string, object)[]
                 {
                     ("approve", new { note = "x" }), ("return", new { reasons = new[] { "OTHER" }, note = "x" }), ("cancel", new { reason = "x" }),
                     ("reassign", new { assigneeId = DevUsers.Technician2, reason = "x" }),
                 })
        {
            var response = await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/{url}", body);
            Assert.Equal("INVALID_TRANSITION", await response.ProblemCodeAsync());
        }

        // N13: a DBA changes the consumed quantity of the closed work order → the verifier names the broken seal (and the balance).
        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
        const string Table = "inventory.stock_movements";
        await db.ExecuteAsOwnerAsync($"ALTER TABLE {Table} DISABLE TRIGGER stock_movements_no_update_delete; " +
                                     $"UPDATE {Table} SET quantity = 3 WHERE work_order_id = '{flow.WorkOrderId}'; " +
                                     $"ALTER TABLE {Table} ENABLE TRIGGER stock_movements_no_update_delete;");
        try
        {
            var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
            Assert.Contains(report.Findings, f => f.Code == "SEAL_MISMATCH" && f.EntityId == flow.WorkOrderId.ToString("D"));
            Assert.Contains(report.Findings, f => f.Code == "STOCK_BALANCE_MISMATCH");
        }
        finally
        {
            await db.ExecuteAsOwnerAsync($"ALTER TABLE {Table} DISABLE TRIGGER stock_movements_no_update_delete; " +
                                         $"UPDATE {Table} SET quantity = 1 WHERE work_order_id = '{flow.WorkOrderId}'; " +
                                         $"ALTER TABLE {Table} ENABLE TRIGGER stock_movements_no_update_delete;");
        }

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task N9_bytes_that_do_not_match_the_announced_hash_are_HASH_MISMATCH_with_a_critical_flag()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.ArriveAsync();
        var bytes = TestJpeg.Create();
        var id = await flow.PhotoAsync("BEFORE", bytes, announcedHash: TestJpeg.Sha256(TestJpeg.Create()));
        var review = await flow.WaitVerifiedAsync();
        var evidence = review.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id);
        Assert.Equal("HASH_MISMATCH", evidence.GetProperty("state").GetString());
        Assert.Contains(review.GetProperty("flags").EnumerateArray(), f => f.GetProperty("ruleCode").GetString() == "EVIDENCE_HASH_MISMATCH" && f.GetProperty("severity").GetString() == "CRITICAL");
        Assert.Equal(1, review.GetProperty("blockedEvidence").GetInt32());

        // It does not count as a before photo; replacing it (supersede with a reason) clears the block.
        var replacement = Guid.CreateVersion7();
        var fresh = TestJpeg.Create();
        await flow.ApplyAsync("AddEvidence", new { workOrderId = flow.WorkOrderId, evidenceId = replacement, kind = "BEFORE", sha256 = TestJpeg.Sha256(fresh), sizeBytes = fresh.LongLength, mime = "image/jpeg", supersedesEvidenceId = id, supersedeReason = "الصورة لم تُرفع بشكل صحيح" });
        (await flow.UploadAsync(replacement, fresh)).EnsureSuccessStatusCode();
        review = await flow.WaitVerifiedAsync();
        Assert.Equal(0, review.GetProperty("blockedEvidence").GetInt32());
        Assert.Equal(replacement, review.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id).GetProperty("supersededBy").GetGuid());
    }

    [Fact]
    public async Task N10_the_same_photo_bytes_on_a_second_work_order_are_DUPLICATE_with_a_critical_flag()
    {
        var bytes = TestJpeg.Create();
        using var first = await FieldFlow.StartAsync(App);
        await first.ArriveAsync();
        await first.PhotoAsync("BEFORE", bytes);
        await first.WaitVerifiedAsync();

        using var second = await FieldFlow.StartAsync(App);
        await second.ArriveAsync();
        var id = await second.PhotoAsync("BEFORE", bytes);
        var review = await second.WaitVerifiedAsync();
        Assert.Equal("DUPLICATE", review.GetProperty("evidence").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id).GetProperty("state").GetString());
        Assert.Contains(review.GetProperty("flags").EnumerateArray(), f => f.GetProperty("ruleCode").GetString() == "EVIDENCE_DUPLICATE" && f.GetProperty("severity").GetString() == "CRITICAL");
    }

    [Fact]
    public async Task N3_approve_is_refused_while_a_photo_is_UPLOADED_but_not_VERIFIED()
    {
        using var manual = new TestApp(db, backgroundVerifier: false);
        using var flow = await FieldFlow.StartAsync(manual);
        await flow.WorkUntilReadyToSubmitAsync();
        await flow.ApplyAsync("SubmitWorkOrder", new { workOrderId = flow.WorkOrderId, rootCauseNote = "سبب" });
        var review = await flow.ReviewAsync();
        Assert.Equal(5, review.GetProperty("evidence").EnumerateArray().Count(e => e.GetProperty("state").GetString() == "UPLOADED"));
        Assert.Equal("EVIDENCE_NOT_VERIFIED", review.GetProperty("approveBlockedReason").GetString());

        using var supervisor = manual.ClientFor(DevUsers.Supervisor);
        var response = await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null });
        Assert.Equal("EVIDENCE_NOT_VERIFIED", await response.ProblemCodeAsync());

        await using (var scope = manual.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EvidenceVerifier>().RunOnceAsync(CancellationToken.None);
        }

        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Return_then_resubmit_adds_only_and_the_reviewer_sees_what_was_added()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.WorkUntilReadyToSubmitAsync();
        await flow.ApplyAsync("SubmitWorkOrder", new { workOrderId = flow.WorkOrderId, rootCauseNote = "سبب" });
        await flow.WaitVerifiedAsync();

        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        Assert.Equal("RETURN_REASONS_REQUIRED", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/return", new { reasons = Array.Empty<string>() })).ProblemCodeAsync());
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/return", new { reasons = new[] { "UNCLEAR_EVIDENCE" }, note = "الصورة بعد الإصلاح غير واضحة" })).EnsureSuccessStatusCode();

        var task = await flow.Phone.GetFromJsonAsync<JsonElement>($"/sync/v1/work-orders/{flow.WorkOrderId}");
        Assert.Equal("RETURNED", task.GetProperty("state").GetString());
        Assert.Equal("UNCLEAR_EVIDENCE", task.GetProperty("progress").GetProperty("lastReturnReasons")[0].GetString());

        var added = await flow.PhotoAsync("AFTER");
        await flow.ApplyAsync("Resubmit", new { workOrderId = flow.WorkOrderId, rootCauseNote = "سبب، مع صورة أوضح" });
        var review = await flow.WaitVerifiedAsync();
        var submissions = review.GetProperty("submissions");
        Assert.Equal(2, submissions.GetArrayLength());
        Assert.Equal(added, submissions[1].GetProperty("addedEvidenceIds")[0].GetGuid());
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();
        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task Consumption_beyond_custody_is_applied_and_flagged_and_N14_issue_beyond_the_warehouse_is_refused()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL" });
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
        var result = await flow.ConsumeAsync(2, [flow.Serials[0], "UNKNOWN-SERIAL-1"]);
        Assert.Equal("APPLIED_EXCEEDS_CUSTODY", result.GetProperty("code").GetString());
        var review = await flow.ReviewAsync();
        Assert.True(review.GetProperty("materials")[0].GetProperty("exceedsCustody").GetBoolean());
        Assert.Contains(review.GetProperty("flags").EnumerateArray(), f => f.GetProperty("ruleCode").GetString() == "CONSUMPTION_EXCEEDS_CUSTODY");
        Assert.Contains(review.GetProperty("flags").EnumerateArray(), f => f.GetProperty("ruleCode").GetString() == "SERIAL_NOT_IN_CUSTODY");

        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        var issue = await warehouse.PostAsJsonAsync("/api/v1/stock/issues", new { technicianId = flow.Technician, lines = new[] { new { itemId = flow.ItemId, quantity = 99, serials = Enumerable.Range(0, 99).Select(i => $"X{i}").ToArray() } } });
        Assert.Equal("INSUFFICIENT_STOCK", await issue.ProblemCodeAsync());
        var balances = await warehouse.GetFromJsonAsync<JsonElement>("/api/v1/stock/balances");
        Assert.Contains(balances.EnumerateArray(), b => b.GetProperty("itemId").GetGuid() == flow.ItemId && b.GetProperty("locationKind").GetString() == "CUSTODY"
                                                        && b.GetProperty("quantity").GetDecimal() == -1m);
        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    [Fact]
    public async Task Checklist_blocking_answer_and_failed_test_stop_the_submission_and_not_tested_needs_a_reason()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL" });
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
        await flow.ConsumeAsync();
        await flow.PhotoAsync("AFTER");
        await flow.PhotoAsync("AFTER");
        await FieldFlow.AssertRejectedAsync(flow.SubmitAsync(), "CHECKLIST_MISSING");
        await flow.ChecklistAsync(overrides: new() { ["EXPOSED_WIRES"] = "YES" });
        await FieldFlow.AssertRejectedAsync(flow.SubmitAsync(), "CHECKLIST_BLOCKING");
        await flow.ChecklistAsync(overrides: new() { ["LAMP_ON"] = "NO" });
        await FieldFlow.AssertRejectedAsync(flow.SubmitAsync(), "TEST_FAILED");
        var template = await flow.TemplateAsync();
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("RecordChecklist", new { workOrderId = flow.WorkOrderId, checklistId = Guid.CreateVersion7(), templateId = template.GetProperty("id").GetGuid(), testMethod = "NOT_TESTED", answers = new Dictionary<string, string>() }), "NOT_TESTED_REASON_REQUIRED");
        await flow.ChecklistAsync("NOT_TESTED", notTestedReason: "النهار ولا يمكن تغطية الخلية");
        Assert.Equal("APPLIED", (await flow.SubmitAsync()).GetProperty("result").GetString()); // no TEST photo needed when not tested
        await flow.WaitVerifiedAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal("UNKNOWN", (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{flow.AssetId}")).GetProperty("operationalStatus").GetString()); // OS7
    }

    [Fact]
    public async Task Evidence_previews_are_authorized_and_originals_are_auditor_only_and_audited()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.ArriveAsync();
        var id = await flow.PhotoAsync("BEFORE");
        await flow.WaitVerifiedAsync();

        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var preview = await supervisor.GetAsync($"/api/v1/evidence/{id}/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/jpeg", preview.Content.Headers.ContentType!.MediaType);
        var bytes = await preview.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xFF, 0xD8], bytes[..2]);

        using var otherTech = App.ClientFor(DevUsers.Technician2);
        Assert.Equal("EVIDENCE_NOT_OWNER", await (await otherTech.GetAsync($"/api/v1/evidence/{id}/preview")).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await flow.Phone.GetAsync($"/api/v1/evidence/{id}/preview?size=thumb")).StatusCode); // own photo

        Assert.Equal("FORBIDDEN", await (await supervisor.GetAsync($"/api/v1/evidence/{id}/original")).ProblemCodeAsync());
        using var auditor = App.ClientFor(DevUsers.Auditor);
        var original = await auditor.GetAsync($"/api/v1/evidence/{id}/original");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var search = await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/audit/events?entityType=evidence&entityId={id}");
        Assert.Contains(search.EnumerateArray(), e => e.GetProperty("action").GetString() == "evidence.original_viewed");
        Assert.Contains(search.EnumerateArray(), e => e.GetProperty("action").GetString() == "evidence.verified");
    }

    [Fact]
    public async Task Upload_is_refused_for_someone_else_or_for_non_JPEG_bytes_and_repeated_uploads_change_nothing()
    {
        using var flow = await FieldFlow.StartAsync(App);
        await flow.ArriveAsync();
        var id = await flow.PhotoAsync("BEFORE", upload: false);
        var otherTech = await App.CreateUserAsync("TECHNICIAN");
        using var otherPhone = App.FieldAppClientFor(otherTech, await App.ActiveDeviceAsync(otherTech));
        var response = await flow.UploadAsync(id, TestJpeg.Create(), otherPhone);
        Assert.Equal("EVIDENCE_NOT_OWNER", await response.ProblemCodeAsync());
        Assert.Equal("EVIDENCE_NOT_JPEG", await (await flow.UploadAsync(id, "not a jpeg"u8.ToArray())).ProblemCodeAsync());
        var bytes = TestJpeg.Create();
        (await flow.UploadAsync(id, bytes)).EnsureSuccessStatusCode();
        var again = await flow.UploadAsync(id, bytes);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }
}
