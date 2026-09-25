using Mfop.Http;
using Mfop.Rules.Application;
using Mfop.Rules.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Rules.Api;

public static class RulesEndpoints
{
    public static IEndpointRouteBuilder MapRulesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/recommendations", async (Guid assetId, IRecommendations r, CancellationToken ct) => Results.Ok(await r.OpenForAssetAsync(assetId, ct)))
            .WithTags("Rules")
            .RequirePermission("recommendation.read");

        var settings = app.MapGroup("/api/v1/settings/rules").WithTags("Rules");
        settings.MapGet("/", async (RuleSettingsService s, CancellationToken ct) => Results.Ok(await s.ListAsync(ct)))
            .RequirePermission("policy.settings.change");
        settings.MapPost("/repeated-failure", async (ChangeRepeatedFailureSettingRequest body, HttpContext http, RuleSettingsService s, CancellationToken ct) =>
                (await s.ChangeRepeatedFailureAsync(http.Command("ChangeRuleSetting", body.Reason), body, ct)).ToHttp(id => Results.Ok(new { id })))
            .RequirePermission("policy.settings.change");
        return app;
    }
}
