using System.Security.Claims;
using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Access.Api;

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", GetMe)
            .RequireAuthorization()
            .WithName("GetMe")
            .WithTags("Access");
        return app;
    }

    private static async Task<IResult> GetMe(ClaimsPrincipal principal, CurrentUserService service, CancellationToken ct)
    {
        var sub = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await service.ResolveAsync(sub, ct);
        return result.Outcome switch
        {
            CurrentUserOutcome.Ok => Results.Ok(result.User),
            CurrentUserOutcome.Disabled => Problem("USER_DISABLED", "User account is disabled."),
            _ => Problem("USER_NOT_PROVISIONED", "Authenticated identity has no user record in this system."),
        };
    }

    private static IResult Problem(string code, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: code,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
