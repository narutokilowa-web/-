using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mfop.Access.Application;
using Mfop.Access.Domain;

namespace Mfop.Api.Tests;

/// <summary>
/// Runs the real API against the test database. Only authentication is replaced:
/// the "X-Test-Sub" header plays the role of a validated token's "sub" claim.
/// (Real Keycloak token validation is verified end-to-end against Docker Compose.)
/// </summary>
public sealed class TestApp(DatabaseFixture db, bool backgroundVerifier = true, ShiftableClock? clock = null) : WebApplicationFactory<Program>
{
    public const string SubHeader = "X-Test-Sub";

    /// <summary>Plays the token's "azp" claim (which client the token was issued to), e.g. the field app "mfop-mobile".</summary>
    public const string AzpHeader = "X-Test-Azp";
    public const string TagCheckKey = "test-tag-check-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:App", db.AppConnectionString);
        builder.UseSetting("Auth:Issuer", "http://issuer.test/realms/mfop");
        builder.UseSetting("Auth:MetadataAddress", "http://issuer.test/realms/mfop/.well-known/openid-configuration");
        builder.UseSetting("Tags:CheckKey", TagCheckKey);
        builder.UseSetting("Tags:BaseUrl", "http://web.test/t");
        builder.UseSetting("Storage:Mode", "local");
        builder.UseSetting("Storage:Root", db.ObjectStoreRoot);
        builder.UseSetting("Evidence:VerifyIntervalSeconds", "0.3");
        builder.UseSetting("Evidence:RunBackgroundVerifier", backgroundVerifier ? "true" : "false");
        builder.UseSetting("Inventory:RunBackgroundTimers", "false"); // tests run InventoryTimers themselves
        builder.UseSetting("Keycloak:RunWorker", "false"); // tests run IdpTaskProcessor themselves, against FakeIdentityProvider
        builder.ConfigureTestServices(services =>
        {
            if (clock is not null) services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<FakeIdentityProvider>();
            services.AddSingleton<IIdentityProviderAdmin>(sp => sp.GetRequiredService<FakeIdentityProvider>());
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                o.DefaultScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubHeader, out var sub) || string.IsNullOrEmpty(sub))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new("sub", sub!) };
            if (Request.Headers.TryGetValue(AzpHeader, out var azp) && !string.IsNullOrEmpty(azp))
            {
                claims.Add(new Claim("azp", azp!));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

/// <summary>Records identity-provider calls instead of calling Keycloak; can be told to fail (retry / alert tests).</summary>
public sealed class FakeIdentityProvider : IIdentityProviderAdmin
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Kind, string Subject)> _calls = new();

    public bool Fail { get; set; }

    public IReadOnlyList<(string Kind, string Subject)> Calls => [.. _calls];

    public Task ApplyAsync(IdpTask task, CancellationToken ct)
    {
        if (Fail) throw new HttpRequestException("identity provider unreachable (test)");
        _calls.Enqueue((task.Kind, task.IdpSubject));
        return Task.CompletedTask;
    }
}

/// <summary>The server's clock, movable forward to play hours passing (grant expiry) without waiting.</summary>
public sealed class ShiftableClock : TimeProvider
{
    public TimeSpan Offset { get; set; }

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;
}
