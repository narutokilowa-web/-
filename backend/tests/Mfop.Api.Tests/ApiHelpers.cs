using System.Net.Http.Json;
using System.Text.Json;
using Mfop.Access.Application;
using Mfop.Kernel.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Api.Tests;

/// <summary>Seeded dev users (deploy/seed/dev-users.json) — one per role.</summary>
public static class DevUsers
{
    public const string Registrar = "0197c000-0000-7000-8000-000000000001";
    public const string Approver = "0197c000-0000-7000-8000-000000000002";
    public const string Supervisor = "0197c000-0000-7000-8000-000000000003";
    public const string Technician = "0197c000-0000-7000-8000-000000000004";
    public const string Warehouse = "0197c000-0000-7000-8000-000000000005";
    public const string Auditor = "0197c000-0000-7000-8000-000000000006";
    public const string Admin = "0197c000-0000-7000-8000-000000000007";
    public const string Inspector = "0197c000-0000-7000-8000-000000000008";
    public const string Technician2 = "0197c000-0000-7000-8000-000000000009";

    public static readonly IReadOnlyDictionary<string, string> ByRole = new Dictionary<string, string>
    {
        ["REGISTRAR"] = Registrar,
        ["APPROVER"] = Approver,
        ["SUPERVISOR"] = Supervisor,
        ["TECHNICIAN"] = Technician,
        ["WAREHOUSE"] = Warehouse,
        ["AUDITOR"] = Auditor,
        ["ADMIN"] = Admin,
        ["INSPECTOR"] = Inspector,
    };
}

public static class ApiHelpers
{
    public static HttpClient ClientFor(this TestApp app, string sub, Guid? idempotencyKey = null)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestApp.SubHeader, sub);
        if (idempotencyKey is { } key)
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString());
        }

        return client;
    }

    public static object SampleRegistration(decimal lat = 33.5138m, decimal lon = 36.2765m, string power = "GRID", string? address = "شارع تجريبي 1", string reason = "BASELINE_SURVEY", string? project = null) => new
    {
        reason,
        projectRef = project,
        attributes = new
        {
            latitude = lat,
            longitude = lon,
            powerSource = power,
            responsibleParty = "MUNICIPALITY",
            installedOnPrecision = "YEAR",
            installedOn = "2019-01-01",
            observedStatus = "OPERATIONAL",
            addressText = address,
            poleMaterial = "STEEL",
            poleHeightM = 8.50m,
            luminaireMake = "Demo",
            luminaireWattageW = 60,
            solarPanelW = power == "GRID" ? (int?)null : 100,
            solarBatteryAh = power == "GRID" ? (int?)null : 100,
        },
    };

    public static async Task<Guid> CreateRegistrationAsync(this HttpClient registrar, object? body = null)
    {
        var response = await registrar.PostAsJsonAsync("/api/v1/registrations", body ?? SampleRegistration());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> PostCommandAsync(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { note = (string?)null });

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var c) ? c.GetString() : null;

    /// <summary>Drafts, submits and approves a street light; returns (registrationId, assetId).</summary>
    public static async Task<(Guid RegistrationId, Guid AssetId)> RegisterAndApproveAsync(this TestApp app, object? body = null)
    {
        using var registrar = app.ClientFor(DevUsers.Registrar);
        using var approver = app.ClientFor(DevUsers.Approver);
        var id = await registrar.CreateRegistrationAsync(body);
        (await registrar.PostCommandAsync($"/api/v1/registrations/{id}/submit")).EnsureSuccessStatusCode();
        (await approver.PostCommandAsync($"/api/v1/registrations/{id}/approve", new { note = "موافق" })).EnsureSuccessStatusCode();
        var registration = await approver.GetFromJsonAsync<JsonElement>($"/api/v1/registrations/{id}");
        return (id, registration.GetProperty("assetId").GetGuid());
    }

    public const string FieldAppClientId = "mfop-mobile";

    /// <summary>A client that behaves like the field app: token issued to mfop-mobile, optional device header.</summary>
    public static HttpClient FieldAppClientFor(this TestApp app, string sub, Guid? deviceId)
    {
        var client = app.ClientFor(sub);
        client.DefaultRequestHeaders.Add(TestApp.AzpHeader, FieldAppClientId);
        if (deviceId is { } d)
        {
            client.DefaultRequestHeaders.Add("X-Device-Id", d.ToString());
        }

        return client;
    }

    /// <summary>Creates a fresh user with the given roles (audited, like the dev bootstrap) so tests do not share state.</summary>
    public static async Task<string> CreateUserAsync(this TestApp app, params string[] roles)
    {
        var id = Guid.CreateVersion7();
        await using var scope = app.Services.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<UserProvisioning>();
        var name = $"test-{id:N}"[..20];
        var result = await provisioning.EnsureUserWithRolesAsync(id, name, name, roles, ActorContext.System, "TEST_SETUP", CancellationToken.None);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error!.Code);
        }

        return id.ToString("D");
    }

    public static async Task<Guid> RegisterDeviceAsync(this TestApp app, string sub, string model = "Test Phone")
    {
        using var client = app.FieldAppClientFor(sub, null);
        var response = await client.PostAsJsonAsync("/sync/v1/devices/register", new { platform = "ANDROID", model, osVersion = "14", appVersion = "0.2.0" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deviceId").GetGuid();
    }

    /// <summary>Registers a device for the user and has admin1 approve it. Returns the ACTIVE device id.</summary>
    public static async Task<Guid> ActiveDeviceAsync(this TestApp app, string sub)
    {
        var deviceId = await app.RegisterDeviceAsync(sub);
        using var admin = app.ClientFor(DevUsers.Admin);
        (await admin.PostAsJsonAsync($"/api/v1/devices/{deviceId}/approve", new { reason = (string?)null })).EnsureSuccessStatusCode();
        return deviceId;
    }

    public static object SampleFault(Guid assetId, string category = "LIGHT_OFF", string power = "CONFIRMED_AVAILABLE", string? description = "لا يضيء ليلًا") =>
        new { assetId, category, powerAtInspection = power, powerInfoSource = "OBSERVED_ON_SITE", description };

    /// <summary>Supervisor reports a fault on a new ACTIVE street light. Returns (assetId, faultId).</summary>
    public static async Task<(Guid AssetId, Guid FaultId)> ActiveAssetWithFaultAsync(this TestApp app, string category = "LIGHT_OFF")
    {
        var (_, assetId) = await app.RegisterAndApproveAsync();
        using var supervisor = app.ClientFor(DevUsers.Supervisor);
        var response = await supervisor.PostAsJsonAsync("/api/v1/faults", SampleFault(assetId, category));
        response.EnsureSuccessStatusCode();
        return (assetId, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("faultId").GetGuid());
    }

    public static async Task<Guid> CreateWorkOrderAsync(this TestApp app, Guid faultId, string priority = "P2")
    {
        using var supervisor = app.ClientFor(DevUsers.Supervisor);
        var response = await supervisor.PostAsJsonAsync("/api/v1/work-orders", new { faultId, priority, note = "تفقد وإصلاح" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<string[]> AuditActionsForCommandAsync(this DatabaseFixture db, Guid commandId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(db.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("SELECT action FROM audit.audit_events WHERE command_id = @c ORDER BY seq", conn);
        cmd.Parameters.AddWithValue("c", commandId);
        var actions = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            actions.Add(r.GetString(0));
        }

        return [.. actions];
    }
}
