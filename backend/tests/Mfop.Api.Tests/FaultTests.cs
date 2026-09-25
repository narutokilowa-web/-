using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mfop.Api.Tests;

/// <summary>Faults (docs/state-machines/fault.yaml) and their effect on the asset's operational status (OS4).</summary>
[Collection(nameof(DatabaseCollection))]
public class FaultTests(DatabaseFixture db) : IAsyncLifetime
{
    private TestApp App { get; } = new(db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    private async Task<string?> StatusOfAsync(Guid assetId)
    {
        using var auditor = App.ClientFor(DevUsers.Auditor);
        return (await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}")).GetProperty("operationalStatus").GetString();
    }

    [Fact]
    public async Task Report_creates_an_open_fault_marks_the_light_faulty_and_audits_it()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        Assert.Equal("OPERATIONAL", await StatusOfAsync(assetId));

        var key = Guid.NewGuid();
        using var supervisor = App.ClientFor(DevUsers.Supervisor, key);
        var response = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CREATED", result.GetProperty("result").GetString());
        Assert.Equal(
            ["fault.reported", "street_light.operational_status_changed", "asset_status_event.recorded"],
            await db.AuditActionsForCommandAsync(key));
        Assert.Equal("FAULTY", await StatusOfAsync(assetId));

        var fault = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{result.GetProperty("faultId").GetGuid()}");
        Assert.Equal("OPEN", fault.GetProperty("state").GetString());
        Assert.Equal("MUNICIPALITY", fault.GetProperty("responsibleParty").GetString());
        Assert.Equal("مشرف تجريبي", fault.GetProperty("reportedByName").GetString());

        using var auditor = App.ClientFor(DevUsers.Auditor);
        var history = await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}/history");
        var status = history.EnumerateArray().Single(e => e.GetProperty("action").GetString() == "street_light.operational_status_changed");
        Assert.Equal("OPERATIONAL", status.GetProperty("changes").GetProperty("operational_status").GetProperty("old").GetString());
        Assert.Equal("FAULTY", status.GetProperty("changes").GetProperty("operational_status").GetProperty("new").GetString());
        Assert.Equal("OS4", status.GetProperty("facts").GetProperty("transition").GetString());
        Assert.Contains(history.EnumerateArray(), e => e.GetProperty("action").GetString() == "fault.reported");
    }

    [Fact]
    public async Task A_duplicate_report_becomes_a_confirmation_of_the_open_fault()
    {
        var (assetId, faultId) = await App.ActiveAssetWithFaultAsync();
        var key = Guid.NewGuid();
        using var supervisor = App.ClientFor(DevUsers.Supervisor, key);
        var response = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId, description: "بلاغ مواطن"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CONFIRMED_EXISTING", result.GetProperty("result").GetString());
        Assert.Equal(faultId, result.GetProperty("faultId").GetGuid());
        Assert.Equal(["fault.confirmed", "fault_confirmation.recorded"], await db.AuditActionsForCommandAsync(key)); // already FAULTY: no status change

        var fault = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{faultId}");
        Assert.Equal(1, fault.GetProperty("confirmationCount").GetInt32());
        Assert.Equal("بلاغ مواطن", fault.GetProperty("confirmations")[0].GetProperty("description").GetString());

        // A different category is a different fault.
        var other = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId, category: "DAMAGED_POLE"));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task Power_confirmed_unavailable_or_category_other_leave_the_status_unchanged_and_nothing_is_auto_dismissed()
    {
        var (_, assetId) = await App.RegisterAndApproveAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var response = await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId, power: "CONFIRMED_UNAVAILABLE"));
        var faultId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("faultId").GetGuid();
        Assert.Equal("OPERATIONAL", await StatusOfAsync(assetId));
        var fault = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{faultId}");
        Assert.True(fault.GetProperty("reportedWithoutPower").GetBoolean());
        Assert.Equal("OPEN", fault.GetProperty("state").GetString());

        (await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(assetId, category: "OTHER"))).EnsureSuccessStatusCode();
        Assert.Equal("OPERATIONAL", await StatusOfAsync(assetId));
    }

    [Fact]
    public async Task Faults_need_an_active_asset_and_valid_values()
    {
        using var registrar = App.ClientFor(DevUsers.Registrar);
        var registrationId = await registrar.CreateRegistrationAsync();
        var pendingAssetId = (await registrar.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{registrationId}")).GetProperty("assetId").GetGuid();

        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        Assert.Equal("ASSET_NOT_ACTIVE", await (await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(pendingAssetId))).ProblemCodeAsync());
        Assert.Equal("ASSET_NOT_FOUND", await (await supervisor.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(Guid.NewGuid()))).ProblemCodeAsync());

        var (_, assetId) = await App.RegisterAndApproveAsync();
        var invalid = await supervisor.PostAsJsonAsync("/api/v1/faults", new { assetId, category = "EXPLODED", powerAtInspection = "MAYBE" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var details = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("details");
        Assert.True(details.TryGetProperty("category", out _));
        Assert.True(details.TryGetProperty("powerAtInspection", out _));
    }

    [Fact]
    public async Task Dismiss_needs_a_listed_reason_a_note_for_OTHER_and_is_terminal()
    {
        var (_, faultId) = await App.ActiveAssetWithFaultAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        Assert.Equal("INVALID_DISMISS_REASON", await (await supervisor.PostAsJsonAsync($"/api/v1/faults/{faultId}/dismiss", new { reason = "BORED" })).ProblemCodeAsync());
        Assert.Equal("NOTE_REQUIRED", await (await supervisor.PostAsJsonAsync($"/api/v1/faults/{faultId}/dismiss", new { reason = "OTHER" })).ProblemCodeAsync());

        var key = Guid.NewGuid();
        using var keyed = App.ClientFor(DevUsers.Supervisor, key);
        (await keyed.PostAsJsonAsync($"/api/v1/faults/{faultId}/dismiss", new { reason = "NOT_A_FAULT", note = "الإنارة تعمل عند الزيارة" })).EnsureSuccessStatusCode();
        Assert.Equal(["fault.dismissed"], await db.AuditActionsForCommandAsync(key));
        var fault = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/faults/{faultId}");
        Assert.Equal("DISMISSED", fault.GetProperty("state").GetString());

        var again = await supervisor.PostAsJsonAsync($"/api/v1/faults/{faultId}/dismiss", new { reason = "DUPLICATE" });
        Assert.Equal("INVALID_TRANSITION", await again.ProblemCodeAsync());
        Assert.Equal("INVALID_TRANSITION", await (await supervisor.PostAsJsonAsync("/api/v1/work-orders", new { faultId, priority = "P2" })).ProblemCodeAsync());
    }

    [Fact]
    public async Task Fault_list_filters_by_state_and_asset()
    {
        var (assetId, faultId) = await App.ActiveAssetWithFaultAsync();
        using var auditor = App.ClientFor(DevUsers.Auditor);
        var byAsset = await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/faults?assetId={assetId}");
        Assert.Equal(faultId, byAsset.EnumerateArray().Single().GetProperty("id").GetGuid());
        var open = await auditor.GetFromJsonAsync<JsonElement>("/api/v1/faults?state=OPEN");
        Assert.All(open.EnumerateArray(), f => Assert.Equal("OPEN", f.GetProperty("state").GetString()));
    }
}
