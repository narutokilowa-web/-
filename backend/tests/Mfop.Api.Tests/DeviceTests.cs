using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mfop.Api.Tests;

/// <summary>BYOD device trust (docs/lean-pilot/05): registration, approval by another user, server-side revocation.</summary>
[Collection(nameof(DatabaseCollection))]
public class DeviceTests(DatabaseFixture db) : IAsyncLifetime
{
    private TestApp App { get; } = new(db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    private async Task<JsonElement> DeviceAsync(Guid id)
    {
        using var admin = App.ClientFor(DevUsers.Admin);
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/devices");
        return all.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Register_then_approve_by_supervisor_makes_the_device_active_and_audits_each_step()
    {
        var tech = await App.CreateUserAsync("TECHNICIAN");
        var key = Guid.NewGuid();
        using var phone = App.FieldAppClientFor(tech, null);
        phone.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString());
        using var deviceKey = new TestPhone();
        var registered = await phone.PostAsJsonAsync("/sync/v1/devices/register",
            new { platform = "ANDROID", model = "Galaxy A12", osVersion = "12", appVersion = "0.5.0", publicKey = deviceKey.PublicKey });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var deviceId = (await registered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deviceId").GetGuid();
        deviceKey.Bind(deviceId);
        Assert.Equal(["device.registered"], await db.AuditActionsForCommandAsync(key));
        Assert.Equal("PENDING_APPROVAL", (await DeviceAsync(deviceId)).GetProperty("status").GetString());

        // While pending, the phone can read its own status but nothing else.
        using var pending = App.FieldAppClientFor(tech, deviceId);
        var status = await pending.GetFromJsonAsync<JsonElement>("/sync/v1/devices/current");
        Assert.Equal("PENDING_APPROVAL", status.GetProperty("status").GetString());
        var pull = await pending.GetAsync("/sync/v1/pull");
        Assert.Equal(HttpStatusCode.Forbidden, pull.StatusCode);
        Assert.Equal("DEVICE_NOT_APPROVED", await pull.ProblemCodeAsync());

        var approveKey = Guid.NewGuid();
        using var supervisor = App.ClientFor(DevUsers.Supervisor, approveKey);
        (await supervisor.PostAsJsonAsync($"/api/v1/devices/{deviceId}/approve", new { reason = (string?)null })).EnsureSuccessStatusCode();
        Assert.Equal(["device.approved"], await db.AuditActionsForCommandAsync(approveKey));
        var device = await DeviceAsync(deviceId);
        Assert.Equal("ACTIVE", device.GetProperty("status").GetString());
        Assert.Equal("مشرف تجريبي", device.GetProperty("decidedByName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await pending.GetAsync("/sync/v1/pull")).StatusCode);
    }

    [Fact]
    public async Task A_new_registration_supersedes_the_pending_one_and_approving_a_new_device_revokes_the_old_active_one()
    {
        var tech = await App.CreateUserAsync("TECHNICIAN");
        var first = await App.ActiveDeviceAsync(tech);
        var pendingA = await App.RegisterDeviceAsync(tech, "Phone A");
        var pendingB = await App.RegisterDeviceAsync(tech, "Phone B");

        var a = await DeviceAsync(pendingA);
        Assert.Equal("REJECTED", a.GetProperty("status").GetString());
        Assert.Equal("SUPERSEDED_BY_NEW_REGISTRATION", a.GetProperty("decisionReason").GetString());

        using var admin = App.ClientFor(DevUsers.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/devices/{pendingB}/approve", new { reason = (string?)null })).EnsureSuccessStatusCode();
        var old = await DeviceAsync(first);
        Assert.Equal("REVOKED", old.GetProperty("status").GetString());
        Assert.Equal("REPLACED", old.GetProperty("revokeReason").GetString());
        Assert.Equal("ACTIVE", (await DeviceAsync(pendingB)).GetProperty("status").GetString());

        using var oldPhone = App.FieldAppClientFor(tech, first);
        var rejected = await oldPhone.GetAsync("/sync/v1/pull");
        Assert.Equal("DEVICE_REVOKED", await rejected.ProblemCodeAsync());
    }

    [Fact]
    public async Task Revocation_is_enforced_on_the_very_next_request_and_the_phone_can_still_learn_its_status()
    {
        var tech = await App.CreateUserAsync("TECHNICIAN");
        var deviceId = await App.ActiveDeviceAsync(tech);
        using var phone = App.FieldAppClientFor(tech, deviceId);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/sync/v1/pull")).StatusCode);

        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        var noReason = await supervisor.PostAsJsonAsync($"/api/v1/devices/{deviceId}/revoke", new { reason = "" });
        Assert.Equal("REASON_REQUIRED", await noReason.ProblemCodeAsync());
        (await supervisor.PostAsJsonAsync($"/api/v1/devices/{deviceId}/revoke", new { reason = "الهاتف مفقود" })).EnsureSuccessStatusCode();

        foreach (var url in new[] { "/sync/v1/pull", "/sync/v1/tags/00000000.00", "/api/v1/assets" })
        {
            var response = await phone.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("DEVICE_REVOKED", await response.ProblemCodeAsync());
        }

        var push = await phone.PostAsJsonAsync("/sync/v1/push", new { commands = Array.Empty<object>() });
        Assert.Equal("DEVICE_REVOKED", await push.ProblemCodeAsync());

        var status = await phone.GetFromJsonAsync<JsonElement>("/sync/v1/devices/current");
        Assert.Equal("REVOKED", status.GetProperty("status").GetString());
        Assert.Equal("الهاتف مفقود", status.GetProperty("reason").GetString());

        // Terminal: a revoked device cannot be approved again.
        var again = await supervisor.PostAsJsonAsync($"/api/v1/devices/{deviceId}/approve", new { reason = (string?)null });
        Assert.Equal("INVALID_TRANSITION", await again.ProblemCodeAsync());
    }

    [Fact]
    public async Task Field_app_tokens_need_a_device_and_the_device_must_belong_to_the_caller()
    {
        var tech = await App.CreateUserAsync("TECHNICIAN");
        using var noDevice = App.FieldAppClientFor(tech, null);
        var response = await noDevice.GetAsync("/api/v1/assets");
        Assert.Equal("DEVICE_REQUIRED", await response.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await noDevice.GetAsync("/api/v1/me")).StatusCode); // identity only

        var otherTech = await App.CreateUserAsync("TECHNICIAN");
        var othersDevice = await App.ActiveDeviceAsync(otherTech);
        using var borrowed = App.FieldAppClientFor(tech, othersDevice);
        Assert.Equal("DEVICE_NOT_FOUND", await (await borrowed.GetAsync("/sync/v1/pull")).ProblemCodeAsync());
    }

    [Fact]
    public async Task Nobody_approves_their_own_device_and_rejection_needs_a_reason()
    {
        var supervisorUser = await App.CreateUserAsync("SUPERVISOR");
        var ownDevice = await App.RegisterDeviceAsync(supervisorUser);
        using var self = App.ClientFor(supervisorUser);
        var selfApproval = await self.PostAsJsonAsync($"/api/v1/devices/{ownDevice}/approve", new { reason = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
        Assert.Equal("SOD_VIOLATION", await selfApproval.ProblemCodeAsync());

        using var admin = App.ClientFor(DevUsers.Admin);
        Assert.Equal("REASON_REQUIRED", await (await admin.PostAsJsonAsync($"/api/v1/devices/{ownDevice}/reject", new { reason = " " })).ProblemCodeAsync());
        (await admin.PostAsJsonAsync($"/api/v1/devices/{ownDevice}/reject", new { reason = "جهاز غير معروف" })).EnsureSuccessStatusCode();
        using var phone = App.FieldAppClientFor(supervisorUser, ownDevice);
        Assert.Equal("DEVICE_REJECTED", await (await phone.GetAsync("/sync/v1/pull")).ProblemCodeAsync());
    }

    [Fact]
    public async Task Invalid_registration_is_rejected_with_details()
    {
        var tech = await App.CreateUserAsync("TECHNICIAN");
        using var phone = App.FieldAppClientFor(tech, null);
        var response = await phone.PostAsJsonAsync("/sync/v1/devices/register", new { platform = "IOS", model = "", osVersion = "1", appVersion = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("details").TryGetProperty("platform", out _));
        Assert.True(body.GetProperty("details").TryGetProperty("model", out _));
    }
}
