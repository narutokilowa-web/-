using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mfop.Access.Application;

/// <summary>Configuration section "Keycloak": a confidential service-account client allowed to manage users of the realm.</summary>
public sealed class IdentityProviderOptions
{
    /// <summary>Keycloak base URL as seen from the API container, e.g. http://keycloak:8080.</summary>
    public string? BaseUrl { get; set; }
    public string Realm { get; set; } = "mfop";
    public string AdminClientId { get; set; } = "mfop-backend";
    public string? AdminClientSecret { get; set; }
    public string MobileClientId { get; set; } = "mfop-mobile";
    public double TaskIntervalSeconds { get; set; } = 5;
    public bool RunWorker { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(AdminClientSecret);
}

/// <summary>Carries out one identity-provider task. Throws on failure (the worker retries).</summary>
public interface IIdentityProviderAdmin
{
    Task ApplyAsync(IdpTask task, CancellationToken ct);
}

/// <summary>
/// Keycloak Admin REST API (docs/lean-pilot/05 §5.6 step 4): disable/enable the user, end all sessions, or end only the
/// field-app sessions (device revocation). The Keycloak user id is the token subject we store as <c>idp_subject</c>.
/// </summary>
public sealed class KeycloakAdmin(HttpClient http, IOptions<IdentityProviderOptions> options) : IIdentityProviderAdmin
{
    public async Task ApplyAsync(IdpTask task, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured) throw new InvalidOperationException("Keycloak admin client is not configured (Keycloak:BaseUrl / AdminClientSecret).");
        var admin = $"{o.BaseUrl!.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(o.Realm)}";
        var user = $"{admin}/users/{Uri.EscapeDataString(task.IdpSubject)}";
        var token = await TokenAsync(o, ct);

        async Task<HttpResponseMessage> Send(HttpMethod method, string url, HttpContent? content = null)
        {
            using var request = new HttpRequestMessage(method, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await http.SendAsync(request, ct);
        }

        switch (task.Kind)
        {
            case IdpTaskKinds.DisableUser:
            case IdpTaskKinds.EnableUser:
                // Read-modify-write: a partial representation could be rejected by the user-profile checks or drop attributes.
                var current = await Send(HttpMethod.Get, user);
                current.EnsureSuccessStatusCode();
                var representation = (await current.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(ct))!;
                representation["enabled"] = task.Kind == IdpTaskKinds.EnableUser;
                (await Send(HttpMethod.Put, user, JsonContent.Create(representation))).EnsureSuccessStatusCode();
                break;
            case IdpTaskKinds.LogoutAll:
                (await Send(HttpMethod.Post, $"{user}/logout")).EnsureSuccessStatusCode();
                break;
            case IdpTaskKinds.LogoutMobile:
                var list = await Send(HttpMethod.Get, $"{user}/sessions");
                list.EnsureSuccessStatusCode();
                var sessions = await list.Content.ReadFromJsonAsync<JsonElement>(ct);
                foreach (var session in sessions.EnumerateArray())
                {
                    var clients = session.TryGetProperty("clients", out var c) ? c : default;
                    var isMobile = clients.ValueKind == JsonValueKind.Object
                                   && clients.EnumerateObject().Any(p => p.Value.GetString() == o.MobileClientId);
                    if (isMobile)
                    {
                        var response = await Send(HttpMethod.Delete, $"{admin}/sessions/{session.GetProperty("id").GetString()}");
                        if (response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
                    }
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown identity-provider task {task.Kind}.");
        }
    }

    private async Task<string> TokenAsync(IdentityProviderOptions o, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = o.AdminClientId,
            ["client_secret"] = o.AdminClientSecret!,
        });
        var response = await http.PostAsync($"{o.BaseUrl!.TrimEnd('/')}/realms/{Uri.EscapeDataString(o.Realm)}/protocol/openid-connect/token", form, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }
}

/// <summary>Sends pending identity-provider tasks after commit, with exponential back-off; after repeated failures the task shows an alert to admins.</summary>
public sealed class IdpTaskProcessor(AccessDbContext db, IIdentityProviderAdmin admin, TimeProvider clock, ILogger<IdpTaskProcessor> log)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await db.IdpTasks.Where(t => t.DoneAt == null && t.NextAttemptAt <= now).OrderBy(t => t.CreatedAt).Take(20).ToListAsync(ct);
        foreach (var task in due)
        {
            try
            {
                await admin.ApplyAsync(task, ct);
                task.Succeeded(clock.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                task.Failed(ex.Message, clock.GetUtcNow());
                var level = task.Alert ? LogLevel.Critical : LogLevel.Warning;
                log.Log(level, ex, "Identity-provider task {Kind} for user {User} failed (attempt {Attempts})", task.Kind, task.UserId, task.Attempts);
            }

            await db.SaveChangesAsync(ct);
        }

        return due.Count;
    }
}

public sealed class IdpTaskWorker(IServiceScopeFactory scopes, IOptions<IdentityProviderOptions> options, ILogger<IdpTaskWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunWorker) return;
        var interval = TimeSpan.FromSeconds(Math.Max(0.2, options.Value.TaskIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IdpTaskProcessor>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Identity-provider task pass failed; retrying");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
