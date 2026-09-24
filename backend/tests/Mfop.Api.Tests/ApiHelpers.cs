using System.Net.Http.Json;
using System.Text.Json;

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
}
