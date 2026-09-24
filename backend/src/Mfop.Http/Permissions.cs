using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Http;

/// <summary>Endpoint metadata: the permission action this endpoint requires (see <see cref="PermissionMatrix"/>).</summary>
public sealed record RequiredPermission(string Action);

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// Requires authentication, a provisioned active actor, and a role allowed for <paramref name="action"/>.
    /// Roles are read from our database (via <see cref="CurrentActor"/>), never from the token.
    /// </summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string action)
    {
        if (!PermissionMatrix.Actions.ContainsKey(action))
        {
            throw new ArgumentException($"Unknown action '{action}'.", nameof(action));
        }

        return builder
            .RequireAuthorization()
            .WithMetadata(new RequiredPermission(action))
            .AddEndpointFilter(async (context, next) =>
            {
                var actor = context.HttpContext.RequestServices.GetRequiredService<CurrentActor>().Actor;
                if (actor is null || !PermissionMatrix.Allows(action, actor.Roles))
                {
                    return Problems.Forbidden(ErrorCodesHttp.Forbidden, $"Your roles do not allow '{action}'.");
                }

                return await next(context);
            });
    }
}

public static class ErrorCodesHttp
{
    public const string Forbidden = "FORBIDDEN";
    public const string UserDisabled = "USER_DISABLED";
}
