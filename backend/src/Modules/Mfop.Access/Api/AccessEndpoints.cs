using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Access.Api;

/// <summary>Marks identity endpoints that any authenticated actor may call, even one without roles.</summary>
public sealed record AnyAuthenticatedActor;

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        // Deliberately not role-gated: a newly provisioned user with no roles must be able to see that.
        app.MapGet("/api/v1/me", GetMe)
            .RequireAuthorization()
            .WithMetadata(new AnyAuthenticatedActor(), new DeviceNotRequired())
            .WithName("GetMe")
            .WithTags("Access");

        // Phone: register itself (→ PENDING_APPROVAL) and read its own status while waiting for approval.
        var sync = app.MapGroup("/sync/v1/devices").WithTags("Devices");
        sync.MapPost("/register", async (RegisterDeviceRequest body, HttpContext http, DeviceService devices, CancellationToken ct) =>
                (await devices.RegisterAsync(http.Command("RegisterDevice"), body, ct))
                .ToHttp(id => Results.Created($"/sync/v1/devices/current", new DeviceStatusDto(id, DeviceStates.PendingApproval, null))))
            .RequirePermission("device.register")
            .WithMetadata(new DeviceNotRequired());
        sync.MapGet("/current", async (HttpContext http, CurrentActor actor, DeviceService devices, CancellationToken ct) =>
            {
                if (!http.Request.Headers.TryGetValue(DeviceHttp.DeviceIdHeader, out var raw) || !Guid.TryParse(raw, out var id))
                {
                    return Problems.Forbidden(DeviceErrorCodes.DeviceRequired, "Send the X-Device-Id header.");
                }

                var device = await devices.FindAsync(id, ct); // ownership already checked by ActorResolutionMiddleware
                return Results.Ok(new DeviceStatusDto(device!.Id, device.Status, device.RevokeReason ?? device.DecisionReason));
            })
            .RequirePermission("device.register")
            .WithMetadata(new DeviceNotRequired());

        // Web: supervisors and admins decide.
        var web = app.MapGroup("/api/v1/devices").WithTags("Devices");
        web.MapGet("/", async (string? status, DeviceService devices, CancellationToken ct) => Results.Ok(await devices.ListAsync(status, ct)))
            .RequirePermission("device.read");
        web.MapPost("/{id:guid}/approve", async (Guid id, DeviceDecisionRequest body, HttpContext http, DeviceService devices, CancellationToken ct) =>
                (await devices.ApproveAsync(http.Command("ApproveDevice", body.Reason), id, body.Reason, ct)).ToHttp(Ok))
            .RequirePermission("device.approve");
        web.MapPost("/{id:guid}/reject", async (Guid id, DeviceDecisionRequest body, HttpContext http, DeviceService devices, CancellationToken ct) =>
                (await devices.RejectAsync(http.Command("RejectDevice", body.Reason), id, body.Reason, ct)).ToHttp(Ok))
            .RequirePermission("device.approve");
        web.MapPost("/{id:guid}/revoke", async (Guid id, DeviceDecisionRequest body, HttpContext http, DeviceService devices, CancellationToken ct) =>
                (await devices.RevokeAsync(http.Command("RevokeDevice", body.Reason), id, body.Reason, ct)).ToHttp(Ok))
            .RequirePermission("device.revoke");
        return app;
    }

    private static IResult Ok(Guid id) => Results.Ok(new { id });

    private static async Task<IResult> GetMe(CurrentActor current, UserProvisioning users, HttpContext http)
    {
        var actor = current.Required;
        var sub = http.User.FindFirst("sub")?.Value ?? actor.UserId!.Value.ToString("D");
        var user = (await users.FindBySubjectAsync(sub, http.RequestAborted))!;
        return Results.Ok(new CurrentUserDto(user.Id, user.Username, user.DisplayName, actor.Roles, "database"));
    }
}
