using System.Security.Claims;
using Mfop.Access.Application;
using Mfop.Access.Domain;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Http;

namespace Mfop.Access.Api;

/// <summary>
/// After authentication: maps the token's "sub" to OUR user record (creating it on first login, audited),
/// rejects disabled users, and sets <see cref="CurrentActor"/> with roles read from the database.
/// </summary>
public sealed class ActorResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, UserProvisioning users, CurrentActor current, TimeProvider clock)
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

            current.Set(new ActorContext(user.Id, [.. user.ActiveRoles(clock.GetUtcNow())]));
        }

        await next(http);
    }
}
