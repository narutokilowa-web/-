using Mfop.Access.Application;
using Mfop.Access.Contracts;
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
            .WithMetadata(new AnyAuthenticatedActor())
            .WithName("GetMe")
            .WithTags("Access");
        return app;
    }

    private static async Task<IResult> GetMe(CurrentActor current, UserProvisioning users, HttpContext http)
    {
        var actor = current.Required;
        var sub = http.User.FindFirst("sub")?.Value ?? actor.UserId!.Value.ToString("D");
        var user = (await users.FindBySubjectAsync(sub, http.RequestAborted))!;
        return Results.Ok(new CurrentUserDto(user.Id, user.Username, user.DisplayName, actor.Roles, "database"));
    }
}
