using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Mfop.Api;

/// <summary>
/// Validates Keycloak access tokens. Tokens provide identity ("sub") only; roles come from our DB (ADR-0003).
/// </summary>
public static class AuthenticationSetup
{
    public sealed class AuthOptions
    {
        /// <summary>Public issuer as seen by browsers and phones, e.g. http://localhost:8080/realms/mfop.</summary>
        public string Issuer { get; set; } = string.Empty;

        /// <summary>Where the API fetches OIDC metadata and signing keys (internal network), e.g. http://keycloak:8080/realms/mfop/.well-known/openid-configuration.</summary>
        public string MetadataAddress { get; set; } = string.Empty;

        public string Audience { get; set; } = "mfop-api";

        public bool RequireHttpsMetadata { get; set; } = true;
    }

    public static IServiceCollection AddMfopAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.MetadataAddress))
        {
            throw new InvalidOperationException("Auth:Issuer and Auth:MetadataAddress are required.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false; // keep "sub" as "sub"
                jwt.MetadataAddress = options.MetadataAddress;
                jwt.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                };
            });

        services.AddAuthorization();
        return services;
    }
}
