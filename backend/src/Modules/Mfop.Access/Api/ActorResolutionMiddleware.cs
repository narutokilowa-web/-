using System.Security.Claims;
using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Mfop.Access.Api;

/// <summary>
/// After authentication: maps the token's "sub" to OUR user record (creating it on first login, audited),
/// rejects disabled users, checks the calling device, and sets <see cref="CurrentActor"/> with roles read from the database.
/// <para>
/// Device rule (docs/lean-pilot/05 §5.4): a request that names a device (header X-Device-Id), and every request made with a
/// field-app token, is accepted only if that device belongs to the caller and is ACTIVE. The check runs on every request,
/// so a revoked device is rejected on its next call. Exceptions are endpoints marked <see cref="DeviceNotRequired"/>.
/// </para>
/// </summary>
public sealed class ActorResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext http, UserProvisioning users, DeviceService devices, CurrentActor current, TimeProvider clock, IOptions<DeviceGuardOptions> options)
    {
        var principal = http.User;
        var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(subject))
        {
            var user = await users.FindBySubjectAsync(subject, http.RequestAborted)
                       ?? await users.ProvisionOnFirstLoginAsync(
                           subject,
                           principal.FindFirstValue("preferred_username") ?? subject,
                           principal.FindFirstValue("name") ?? principal.FindFirstValue("preferred_username") ?? subject,
                           http.RequestAborted);

            if (user.Status != UserStatus.Active)
            {
                await Problems.Forbidden(ErrorCodesHttp.UserDisabled, "User account is disabled.").ExecuteAsync(http);
                return;
            }

            var deviceOptional = http.GetEndpoint()?.Metadata.GetMetadata<DeviceNotRequired>() is not null;
            var fromFieldApp = principal.FindFirstValue("azp") == options.Value.MobileClientId;
            Guid? deviceId = null;
            if (http.Request.Headers.TryGetValue(DeviceHttp.DeviceIdHeader, out var raw))
            {
                var device = Guid.TryParse(raw, out var parsed) ? await devices.FindAsync(parsed, http.RequestAborted) : null;
                if (device is null || device.UserId != user.Id)
                {
                    await Problems.Forbidden(DeviceErrorCodes.DeviceNotFound, "Unknown device for this user.").ExecuteAsync(http);
                    return;
                }

                if (!device.IsActive && !deviceOptional)
                {
                    var (code, detail) = device.Status switch
                    {
                        DeviceStates.Revoked => (DeviceErrorCodes.DeviceRevoked, "This device has been revoked."),
                        DeviceStates.Rejected => (DeviceErrorCodes.DeviceRejected, "This device registration was rejected."),
                        _ => (DeviceErrorCodes.DeviceNotApproved, "This device is waiting for approval."),
                    };
                    await Problems.Forbidden(code, detail).ExecuteAsync(http);
                    return;
                }

                deviceId = device.IsActive ? device.Id : null;
            }
            else if (fromFieldApp && !deviceOptional)
            {
                await Problems.Forbidden(DeviceErrorCodes.DeviceRequired, "Field-app requests must come from an approved device.").ExecuteAsync(http);
                return;
            }

            current.Set(new ActorContext(user.Id, [.. user.ActiveRoles(clock.GetUtcNow())], deviceId));
        }

        await next(http);
    }
}
