using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mfop.Api.Tests;

/// <summary>Work orders W1–W4 and assignments A1–A3 (docs/state-machines), plus N5 and N15.</summary>
[Collection(nameof(DatabaseCollection))]
public class WorkOrderTests(DatabaseFixture db)
{
    private TestApp App { get; } = new(db);

    private async Task<JsonElement> WorkOrderAsync(Guid id)
    {
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        return await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{id}");
    }

    private async Task<string?> FaultStateAsync(Guid faultId)
    {
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        return (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{faultId}")).GetProperty("state").GetString();
    }

    [Fact]
    public async Task Create_from_an_open_fault_moves_the_fault_into_the_work_order()
    {
        var (_, faultId) = await App.ActiveAssetWithFaultAsync();
        var key = Guid.NewGuid();
        using var supervisor = App.ClientFor(DevUsers.Supervisor, key);
        var response = await supervisor.PostAsJsonAsync("/api/v1/work-orders", new { faultId, priority = "P1", note = "عاجل" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(["work_order.created", "fault.in_work_order"], await db.AuditActionsForCommandAsync(key));

        var wo = await WorkOrderAsync(id);
        Assert.Equal("NEW", wo.GetProperty("state").GetString());
        Assert.Matches(new Regex(@"^WO-\d{4}-\d{6}$"), wo.GetProperty("code").GetString()!);
        Assert.Equal(JsonValueKind.Null, wo.GetProperty("slaDueAt").ValueKind); // no SLA configured: none invented
        Assert.Equal("IN_WORK_ORDER", wo.GetProperty("faultState").GetString());
        Assert.Equal(["Assign", "Cancel"], wo.GetProperty("allowedCommands").EnumerateArray().Select(c => c.GetString()).Order());
        Assert.Equal("IN_WORK_ORDER", await FaultStateAsync(faultId));

        var second = await supervisor.PostAsJsonAsync("/api/v1/work-orders", new { faultId, priority = "P2" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("WORK_ORDER_EXISTS", await second.ProblemCodeAsync());
        Assert.Equal("INVALID_PRIORITY", await (await supervisor.PostAsJsonAsync("/api/v1/work-orders", new { faultId, priority = "URGENT" })).ProblemCodeAsync());
    }

    [Fact]
    public async Task N15_no_work_order_for_an_asset_that_is_not_active()
    {
        var (assetId, faultId) = await App.ActiveAssetWithFaultAsync();
        // Simulates a later lifecycle change (e.g. UNDER_INVESTIGATION, M6): the only way to get an open fault on a non-active asset today.
        await db.ExecuteAsOwnerAsync($"UPDATE assets.street_lights SET lifecycle_state = 'PENDING' WHERE id = '{assetId}'");
        try
        {
            using var supervisor = App.ClientFor(DevUsers.Supervisor);
            var response = await supervisor.PostAsJsonAsync("/api/v1/work-orders", new { faultId, priority = "P2" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("ASSET_NOT_ACTIVE", await response.ProblemCodeAsync());
            Assert.Equal("OPEN", await FaultStateAsync(faultId));
        }
        finally
        {
            await db.ExecuteAsOwnerAsync($"UPDATE assets.street_lights SET lifecycle_state = 'ACTIVE' WHERE id = '{assetId}'");
        }
    }

    [Fact]
    public async Task Assign_only_to_an_active_technician_with_an_active_device()
    {
        var (_, faultId) = await App.ActiveAssetWithFaultAsync();
        var id = await App.CreateWorkOrderAsync(faultId);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);

        var noDevice = await App.CreateUserAsync("TECHNICIAN");
        Assert.Equal("ASSIGNEE_NO_ACTIVE_DEVICE", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = noDevice })).ProblemCodeAsync());
        Assert.Equal("ASSIGNEE_NOT_TECHNICIAN", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = DevUsers.Inspector })).ProblemCodeAsync());
        Assert.Equal("ASSIGNEE_NOT_FOUND", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = Guid.NewGuid() })).ProblemCodeAsync());

        var tech = await App.CreateUserAsync("TECHNICIAN");
        await App.ActiveDeviceAsync(tech);
        var technicians = await supervisor.GetFromJsonAsync<JsonElement>("/api/v1/technicians");
        Assert.True(technicians.EnumerateArray().Single(t => t.GetProperty("userId").GetString() == tech).GetProperty("hasActiveDevice").GetBoolean());
        Assert.False(technicians.EnumerateArray().Single(t => t.GetProperty("userId").GetString() == noDevice).GetProperty("hasActiveDevice").GetBoolean());

        var key = Guid.NewGuid();
        using var keyed = App.ClientFor(DevUsers.Supervisor, key);
        (await keyed.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = tech })).EnsureSuccessStatusCode();
        Assert.Equal(["work_order.assigned", "work_order_assignment.started"], await db.AuditActionsForCommandAsync(key));
        var wo = await WorkOrderAsync(id);
        Assert.Equal("ASSIGNED", wo.GetProperty("state").GetString());
        Assert.Equal(tech, wo.GetProperty("assigneeId").GetString());
        Assert.Equal("ACTIVE", wo.GetProperty("assignments")[0].GetProperty("state").GetString());

        var twice = await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = tech });
        Assert.Equal("INVALID_TRANSITION", await twice.ProblemCodeAsync()); // W2 only from NEW; use Reassign
    }

    [Fact]
    public async Task Reassign_needs_a_reason_and_a_different_technician_and_the_old_assignee_loses_access_N5()
    {
        var (_, faultId) = await App.ActiveAssetWithFaultAsync();
        var id = await App.CreateWorkOrderAsync(faultId);
        var techA = await App.CreateUserAsync("TECHNICIAN");
        var deviceA = await App.ActiveDeviceAsync(techA);
        var techB = await App.CreateUserAsync("TECHNICIAN");
        var deviceB = await App.ActiveDeviceAsync(techB);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = techA })).EnsureSuccessStatusCode();

        using var phoneA = App.FieldAppClientFor(techA, deviceA);
        using var phoneB = App.FieldAppClientFor(techB, deviceB);
        Assert.Equal(HttpStatusCode.OK, (await phoneA.GetAsync($"/sync/v1/work-orders/{id}")).StatusCode);
        var n5 = await phoneB.GetAsync($"/sync/v1/work-orders/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, n5.StatusCode);
        Assert.Equal("NOT_ASSIGNEE", await n5.ProblemCodeAsync());

        Assert.Equal("REASON_REQUIRED", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/reassign", new { assigneeId = techB })).ProblemCodeAsync());
        Assert.Equal("SAME_ASSIGNEE", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/reassign", new { assigneeId = techA, reason = "x" })).ProblemCodeAsync());

        var key = Guid.NewGuid();
        using var keyed = App.ClientFor(DevUsers.Supervisor, key);
        (await keyed.PostAsJsonAsync($"/api/v1/work-orders/{id}/reassign", new { assigneeId = techB, reason = "الفني في إجازة" })).EnsureSuccessStatusCode();
        Assert.Equal(["work_order_assignment.ended", "work_order.reassigned", "work_order_assignment.started"], await db.AuditActionsForCommandAsync(key));

        var wo = await WorkOrderAsync(id);
        var assignments = wo.GetProperty("assignments").EnumerateArray().ToList();
        Assert.Equal(["ENDED", "ACTIVE"], assignments.Select(a => a.GetProperty("state").GetString()));
        Assert.Equal("الفني في إجازة", assignments[0].GetProperty("endReason").GetString());

        Assert.Equal("NOT_ASSIGNEE", await (await phoneA.GetAsync($"/sync/v1/work-orders/{id}")).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await phoneB.GetAsync($"/sync/v1/work-orders/{id}")).StatusCode);
        Assert.DoesNotContain((await phoneA.GetFromJsonAsync<JsonElement>("/sync/v1/pull")).GetProperty("workOrders").EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Cancel_needs_a_reason_reopens_the_fault_ends_the_assignment_and_is_terminal()
    {
        var (_, faultId) = await App.ActiveAssetWithFaultAsync();
        var id = await App.CreateWorkOrderAsync(faultId);
        var tech = await App.CreateUserAsync("TECHNICIAN");
        await App.ActiveDeviceAsync(tech);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = tech })).EnsureSuccessStatusCode();

        Assert.Equal("REASON_REQUIRED", await (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/cancel", new { reason = "" })).ProblemCodeAsync());
        var key = Guid.NewGuid();
        using var keyed = App.ClientFor(DevUsers.Supervisor, key);
        (await keyed.PostAsJsonAsync($"/api/v1/work-orders/{id}/cancel", new { reason = "أنشئ بالخطأ" })).EnsureSuccessStatusCode();
        Assert.Equal(["work_order.cancelled", "work_order_assignment.ended", "fault.reopened_after_cancellation"], await db.AuditActionsForCommandAsync(key));

        var wo = await WorkOrderAsync(id);
        Assert.Equal("CANCELLED", wo.GetProperty("state").GetString());
        Assert.Equal("ENDED", wo.GetProperty("assignments")[0].GetProperty("state").GetString());
        Assert.Empty(wo.GetProperty("allowedCommands").EnumerateArray());
        Assert.Equal("OPEN", await FaultStateAsync(faultId));

        foreach (var (url, body) in new (string, object)[]
                 {
                     ($"/api/v1/work-orders/{id}/cancel", new { reason = "again" }),
                     ($"/api/v1/work-orders/{id}/assign", new { assigneeId = tech }),
                     ($"/api/v1/work-orders/{id}/reassign", new { assigneeId = DevUsers.Technician, reason = "x" }),
                 })
        {
            var response = await supervisor.PostAsJsonAsync(url, body);
            Assert.True(await response.ProblemCodeAsync() is "INVALID_TRANSITION" or "ASSIGNEE_NO_ACTIVE_DEVICE", url);
        }

        // The reopened fault can get a new work order.
        await App.CreateWorkOrderAsync(faultId);
    }

    [Fact]
    public async Task A_duplicate_report_on_a_fault_in_a_work_order_keeps_it_there_F7()
    {
        var (assetId, faultId) = await App.ActiveAssetWithFaultAsync();
        await App.CreateWorkOrderAsync(faultId);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var response = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId));
        Assert.Equal("CONFIRMED_EXISTING", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetString());
        Assert.Equal("IN_WORK_ORDER", await FaultStateAsync(faultId));
    }

    [Fact]
    public async Task Pull_tag_card_and_timeline_show_the_assigned_task()
    {
        var (assetId, faultId) = await App.ActiveAssetWithFaultAsync();
        var id = await App.CreateWorkOrderAsync(faultId, "P1");
        var tech = await App.CreateUserAsync("TECHNICIAN");
        var device = await App.ActiveDeviceAsync(tech);
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{id}/assign", new { assigneeId = tech })).EnsureSuccessStatusCode();

        using var phone = App.FieldAppClientFor(tech, device);
        var pull = await phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull");
        var task = pull.GetProperty("workOrders").EnumerateArray().Single();
        Assert.Equal(id, task.GetProperty("id").GetGuid());
        Assert.Equal("P1", task.GetProperty("priority").GetString());
        Assert.Equal("LIGHT_OFF", task.GetProperty("faultCategory").GetString());
        Assert.Equal(assetId, task.GetProperty("asset").GetProperty("id").GetGuid());
        Assert.Equal(device, pull.GetProperty("deviceId").GetGuid());
        Assert.Equal(["TECHNICIAN"], pull.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        using var approver = App.ClientFor(DevUsers.Approver);
        var asset = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}");
        var humanCode = asset.GetProperty("tag").GetProperty("humanCode").GetString();
        var card = await phone.GetFromJsonAsync<JsonElement>($"/sync/v1/tags/{humanCode}");
        Assert.Equal(asset.GetProperty("assetCode").GetString(), card.GetProperty("asset").GetProperty("assetCode").GetString());
        Assert.Equal("FAULTY", card.GetProperty("asset").GetProperty("operationalStatus").GetString());
        Assert.Equal(1, card.GetProperty("openFaults").GetInt32());
        Assert.Equal(id, card.GetProperty("myOpenTasks")[0].GetProperty("id").GetGuid());
        Assert.NotEmpty(card.GetProperty("recentEvents").EnumerateArray());
        Assert.Equal("TAG_NOT_FOUND", await (await phone.GetAsync($"/sync/v1/tags/{humanCode![..^2]}ZZ")).ProblemCodeAsync());

        var timeline = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{id}/timeline");
        var actions = timeline.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("fault.reported", actions);
        Assert.Contains("work_order.created", actions);
        Assert.Contains("work_order.assigned", actions);
        Assert.Contains("work_order_assignment.started", actions);

        var history = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}/history");
        Assert.Contains(history.EnumerateArray(), e => e.GetProperty("action").GetString() == "work_order.assigned");
    }
}
