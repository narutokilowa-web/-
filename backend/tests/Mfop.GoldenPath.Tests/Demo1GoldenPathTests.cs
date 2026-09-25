using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mfop.Api.Tests;
using Mfop.AuditVerify;

namespace Mfop.GoldenPath.Tests;

/// <summary>
/// DEMO 1 (docs/lean-pilot/11 §11.1) steps 2–19 through the public API, on a database seeded like <c>make seed</c>.
/// Step 1 is Docker Compose itself and step 20 (manual tampering) is in docs/runbooks/demo-1.md and FieldExecutionTests (N13).
/// The phone is played by a client with a field-app token and an ACTIVE device, using the single write path /sync/v1/push.
/// </summary>
public class Demo1GoldenPathTests(GoldenPathFixture fixture) : IClassFixture<GoldenPathFixture>, IAsyncLifetime
{
    private TestApp App { get; } = new(fixture.Db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    [Fact]
    public async Task Demo1_steps_2_to_19()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        using var approver = App.ClientFor(DevUsers.Approver);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        using var auditor = App.ClientFor(DevUsers.Auditor);

        // The seeded database verifies before anything else happens.
        Assert.True((await Verifier.RunAsync(fixture.Db.ReadOnlyConnectionString)).Ok, "the seeded demo database must verify");

        // 2. Log in as asset registrar (identity from our own users table).
        var me = await registrar.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Contains("REGISTRAR", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // 3. Register a synthetic street light.
        var registrationId = await registrar.CreateRegistrationAsync(ApiHelpers.SampleRegistration(address: "شارع العرض — العمود الذهبي"));
        (await registrar.PostCommandAsync($"/api/v1/registrations/{registrationId}/submit")).EnsureSuccessStatusCode();
        // N1: the maker cannot check their own registration.
        Assert.Equal(HttpStatusCode.Forbidden, (await registrar.PostCommandAsync($"/api/v1/registrations/{registrationId}/approve", new { note = "x" })).StatusCode);

        // 4. Another role (approver) approves it.
        (await approver.PostCommandAsync($"/api/v1/registrations/{registrationId}/approve", new { note = "موافق" })).EnsureSuccessStatusCode();
        var assetId = (await approver.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{registrationId}")).GetProperty("assetId").GetGuid();

        // 5. Print / show its QR.
        var asset = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}");
        var tag = asset.GetProperty("tag");
        var humanCode = tag.GetProperty("humanCode").GetString()!;
        Assert.Equal($"http://web.test/t/{humanCode}", tag.GetProperty("qrPayload").GetString());

        // 6. Scan that QR from Android (tech1's phone, approved by admin1).
        var deviceId = await App.ActiveDeviceAsync(DevUsers.Technician);
        using var phone = App.FieldAppClientFor(DevUsers.Technician, deviceId);

        // 7. See the asset record.
        var card = await phone.GetFromJsonAsync<JsonElement>($"/sync/v1/tags/{humanCode}");
        Assert.Equal(assetId, card.GetProperty("asset").GetProperty("id").GetGuid());

        // 8. Create a fault (supervisor).
        var faultResponse = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId));
        faultResponse.EnsureSuccessStatusCode();
        var faultId = (await faultResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("faultId").GetGuid();

        // 9. Create and assign a work order; it appears on the technician's phone.
        var workOrderId = await App.CreateWorkOrderAsync(faultId);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{workOrderId}/assign", new { assigneeId = DevUsers.Technician, reason = (string?)null })).EnsureSuccessStatusCode();
        var pull = await phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull");
        Assert.Contains(pull.GetProperty("workOrders").EnumerateArray(), t => t.GetProperty("id").GetGuid() == workOrderId);

        // 10–16 on the phone.
        var field = new Phone(phone, workOrderId);
        await field.RepairAsync(humanCode, pull, serial: "M-0003");

        // 17. Supervisor approves and closes (the executing technician cannot: N2).
        var review = await field.WaitVerifiedAsync(supervisor);
        Assert.True(review.GetProperty("canApprove").GetBoolean());
        var material = Assert.Single(review.GetProperty("materials").EnumerateArray());
        Assert.Equal("LED-MOD-50W", material.GetProperty("sku").GetString());
        Assert.Equal("SYP", material.GetProperty("totalCost").GetProperty("currency").GetString()); // Money = amount + currency + date, from the seed
        using (var techOnWeb = App.ClientFor(DevUsers.Technician))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await techOnWeb.PostAsJsonAsync($"/api/v1/work-orders/{workOrderId}/approve", new { note = (string?)null })).StatusCode);
        }

        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{workOrderId}/approve", new { note = "تم التحقق" })).EnsureSuccessStatusCode();
        var closed = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{workOrderId}/review");
        Assert.Equal("CLOSED", closed.GetProperty("state").GetString());
        Assert.Matches("^[0-9a-f]{64}$", closed.GetProperty("sealHash").GetString());
        Assert.Equal("RESOLVED", (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{faultId}")).GetProperty("state").GetString());
        Assert.Equal("OPERATIONAL", (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}")).GetProperty("operationalStatus").GetString());
        // One counted failure on a new light is not a recurrent failure.
        Assert.Empty((await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/recommendations?assetId={assetId}")).EnumerateArray());

        // 17a. The seeded light with two earlier repairs: the third counted failure produces the recommendation.
        var repeat = Assert.Single((await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets?q={Uri.EscapeDataString("العمود المتكرر العطل")}")).EnumerateArray());
        var repeatId = repeat.GetProperty("id").GetGuid();
        Assert.Empty((await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/recommendations?assetId={repeatId}")).EnumerateArray());
        var repeatFault = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(repeatId));
        repeatFault.EnsureSuccessStatusCode();
        var repeatWorkOrderId = await App.CreateWorkOrderAsync((await repeatFault.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("faultId").GetGuid());
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{repeatWorkOrderId}/assign", new { assigneeId = DevUsers.Technician, reason = (string?)null })).EnsureSuccessStatusCode();
        var repeatTag = (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{repeatId}")).GetProperty("tag").GetProperty("humanCode").GetString()!;
        var repeatField = new Phone(phone, repeatWorkOrderId);
        await repeatField.RepairAsync(repeatTag, await phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull"), serial: "M-0004");
        await repeatField.WaitVerifiedAsync(supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{repeatWorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();

        var recommendation = Assert.Single((await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/recommendations?assetId={repeatId}")).EnumerateArray());
        Assert.Equal("FULL_INSPECTION", recommendation.GetProperty("code").GetString());
        Assert.Equal(3, recommendation.GetProperty("count").GetInt32());
        Assert.Equal(3, recommendation.GetProperty("threshold").GetInt32());   // from rule_settings (seeded default)
        Assert.Equal(180, recommendation.GetProperty("windowDays").GetInt32());
        Assert.Equal(3, recommendation.GetProperty("countedFailures").GetArrayLength());
        Assert.Contains(recommendation.GetProperty("countedFailures").EnumerateArray(), c => c.GetProperty("workOrderId").GetGuid() == repeatWorkOrderId);
        // ... and on the phone's asset card.
        var repeatCard = await phone.GetFromJsonAsync<JsonElement>($"/sync/v1/tags/{repeatTag}");
        Assert.Equal("FULL_INSPECTION", Assert.Single(repeatCard.GetProperty("recommendations").EnumerateArray()).GetProperty("code").GetString());

        // 18. The web dashboard shows the completed work order.
        var dashboard = await supervisor.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/summary");
        var completed = dashboard.GetProperty("recentlyCompleted").EnumerateArray().ToList();
        Assert.Contains(completed, c => c.GetProperty("id").GetGuid() == workOrderId && c.GetProperty("sealHash").GetString() == closed.GetProperty("sealHash").GetString());
        Assert.Contains(completed, c => c.GetProperty("id").GetGuid() == repeatWorkOrderId);
        Assert.Contains(dashboard.GetProperty("lightsByOperationalStatus").EnumerateArray(), s => s.GetProperty("key").GetString() == "OPERATIONAL");

        // 19. Audit history shows every important change: the work-order timeline (actor, device, hash) and the auditor's search.
        var timeline = (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{workOrderId}/timeline")).EnumerateArray().ToList();
        var actions = timeline.Select(e => e.GetProperty("action").GetString()).ToHashSet();
        foreach (var expected in new[]
                 {
                     "work_order.created", "work_order.assigned", "work_order.arrived_on_site", "work_order.evidence_added", "evidence.announced",
                     "evidence.verified", "work_order.diagnosis_recorded", "work_order.work_started", "work_order.material_consumed",
                     "stock_movement.consume", "work_order.checklist_recorded", "checklist_result.recorded", "work_order.submitted", "work_order.closed",
                 })
        {
            Assert.Contains(expected, actions);
        }

        Assert.All(timeline, e => Assert.Matches("^[0-9a-f]{64}$", e.GetProperty("hash").GetString()));
        Assert.Contains(timeline, e => e.GetProperty("action").GetString() == "work_order.arrived_on_site"
                                       && e.GetProperty("deviceId").GetGuid() == deviceId);
        var auditSearch = (await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/audit/events?entityType=work_order&entityId={workOrderId}")).EnumerateArray().ToList();
        Assert.Contains(auditSearch, e => e.GetProperty("action").GetString() == "work_order.closed"
                                          && e.GetProperty("facts").GetProperty("seal_hash").GetString() == closed.GetProperty("sealHash").GetString());
        var assetHistory = (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}/history")).EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()).ToHashSet();
        Assert.Contains("fault.reported", assetHistory);
        Assert.Contains("fault.resolved", assetHistory);

        // The whole demo database still verifies (chain, snapshots, seals, stock balances).
        var report = await Verifier.RunAsync(fixture.Db.ReadOnlyConnectionString);
        Assert.True(report.Ok, string.Join("; ", report.Findings.Select(f => $"{f.Code} {f.EntityId} {f.Message}")));
        Assert.True(report.WorkOrdersChecked >= 4);
    }

    /// <summary>tech1's phone working on one work order: every write goes through POST /sync/v1/push.</summary>
    private sealed class Phone(HttpClient client, Guid workOrderId)
    {
        public async Task<JsonElement> ApplyAsync(string type, object payload)
        {
            var response = await client.PostAsJsonAsync("/sync/v1/push", new
            {
                commands = new[] { new { commandId = Guid.CreateVersion7(), type, payload, deviceTime = DateTimeOffset.UtcNow } },
            });
            response.EnsureSuccessStatusCode();
            var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0];
            Assert.True(result.GetProperty("result").GetString() == "APPLIED", $"{type}: {result}");
            return result;
        }

        private async Task PhotoAsync(string kind)
        {
            var bytes = TestJpeg.Create();
            var id = Guid.CreateVersion7();
            await ApplyAsync("AddEvidence", new { workOrderId, evidenceId = id, kind, sha256 = TestJpeg.Sha256(bytes), sizeBytes = bytes.LongLength, mime = "image/jpeg" });
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            (await client.PutAsync($"/sync/v1/evidence/{id}/content", content)).EnsureSuccessStatusCode();
        }

        /// <summary>Steps 10–16: arrive (tag scan), before photos, diagnosis, start work, one material from custody, checklist + test photo, after photos, submit.</summary>
        public async Task RepairAsync(string scannedToken, JsonElement pull, string serial)
        {
            var task = pull.GetProperty("workOrders").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == workOrderId);
            var templateId = task.GetProperty("progress").GetProperty("checklistTemplateId").GetGuid();
            var template = pull.GetProperty("reference").GetProperty("checklistTemplates").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == templateId);
            var power = task.GetProperty("asset").GetProperty("powerSource").GetString();

            await ApplyAsync("ArriveOnSite", new { workOrderId, scannedToken });

            // 10. Before evidence.
            await PhotoAsync("BEFORE");
            await PhotoAsync("BEFORE");

            // 11. Diagnosis from the seeded codes for this light's power source.
            var diagnosis = pull.GetProperty("reference").GetProperty("diagnosisCodes").EnumerateArray()
                .Single(d => d.GetProperty("code").GetString() == "LED_MODULE_FAIL");
            Assert.Contains(power, diagnosis.GetProperty("powerSources").EnumerateArray().Select(p => p.GetString()));
            await ApplyAsync("RecordDiagnosis", new { workOrderId, code = "LED_MODULE_FAIL", note = "وحدة الليد لا تعمل" });

            // 12. Start work.
            await ApplyAsync("StartWork", new { workOrderId });

            // 13. One material from the technician's custody (serial-tracked).
            var custody = pull.GetProperty("custody").EnumerateArray().Single(c => c.GetProperty("sku").GetString() == "LED-MOD-50W");
            Assert.Contains(serial, custody.GetProperty("serials").EnumerateArray().Select(s => s.GetString()));
            await ApplyAsync("ConsumeMaterial", new { workOrderId, movementId = Guid.CreateVersion7(), itemId = custody.GetProperty("itemId").GetGuid(), quantity = 1, serials = new[] { serial } });

            // 14. Functional-test checklist (pinned template) + test photo.
            var answers = template.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("expected").GetString()!);
            await ApplyAsync("RecordChecklist", new { workOrderId, checklistId = Guid.CreateVersion7(), templateId, testMethod = "NIGHT", notTestedReason = (string?)null, answers });
            await PhotoAsync("TEST");

            // 15. After evidence.
            await PhotoAsync("AFTER");
            await PhotoAsync("AFTER");

            // 16. Submit.
            await ApplyAsync("SubmitWorkOrder", new { workOrderId, rootCauseNote = "احتراق وحدة الليد", noMaterialReason = (string?)null });
        }

        public async Task<JsonElement> WaitVerifiedAsync(HttpClient supervisor)
        {
            for (var i = 0; i < 100; i++)
            {
                var review = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{workOrderId}/review");
                if (review.GetProperty("pendingEvidence").GetInt32() == 0) return review;
                await Task.Delay(200);
            }

            throw new TimeoutException("evidence was not verified in time");
        }
    }
}
