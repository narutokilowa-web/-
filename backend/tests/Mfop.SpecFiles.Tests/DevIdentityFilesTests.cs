using System.Text.Json;

namespace Mfop.SpecFiles.Tests;

/// <summary>deploy/seed/dev-users.json (our DB) and deploy/keycloak/realm-mfop.json (Keycloak) must agree.</summary>
public class DevIdentityFilesTests
{
    private static JsonElement DevUsers() =>
        JsonDocument.Parse(File.ReadAllText(RepoPaths.Path("deploy", "seed", "dev-users.json"))).RootElement.GetProperty("users");

    private static JsonElement Realm() =>
        JsonDocument.Parse(File.ReadAllText(RepoPaths.Path("deploy", "keycloak", "realm-mfop.json"))).RootElement;

    [Fact]
    public void Every_role_has_at_least_one_dev_user()
    {
        var perms = Yaml.Load<PermissionsSpec>(RepoPaths.Path("docs", "lean-pilot", "permissions.yaml"));
        var covered = DevUsers().EnumerateArray().SelectMany(u => u.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!)).ToHashSet();
        Assert.Empty(perms.Roles.Keys.Except(covered));
    }

    [Fact]
    public void Dev_users_do_not_violate_static_separation_of_duties()
    {
        var perms = Yaml.Load<PermissionsSpec>(RepoPaths.Path("docs", "lean-pilot", "permissions.yaml"));
        foreach (var user in DevUsers().EnumerateArray())
        {
            var roles = user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToHashSet();
            Assert.All(roles, r => Assert.Contains(r, perms.Roles.Keys));
            Assert.DoesNotContain(perms.StaticSod, pair => roles.Contains(pair[0]) && roles.Contains(pair[1]));
        }
    }

    [Fact]
    public void Dev_users_and_keycloak_realm_users_match_by_id_and_username()
    {
        var ours = DevUsers().EnumerateArray()
            .Select(u => (Id: u.GetProperty("id").GetString(), Username: u.GetProperty("username").GetString()))
            .OrderBy(x => x.Id).ToList();
        var keycloak = Realm().GetProperty("users").EnumerateArray()
            .Where(u => !u.TryGetProperty("serviceAccountClientId", out _))
            .Select(u => (Id: u.GetProperty("id").GetString(), Username: u.GetProperty("username").GetString()))
            .OrderBy(x => x.Id).ToList();
        Assert.Equal(ours, keycloak);
        Assert.Equal(ours.Count, ours.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void Realm_does_not_define_business_roles_or_committed_passwords()
    {
        var realm = Realm();
        Assert.False(realm.TryGetProperty("roles", out _), "Business roles live in our DB, not Keycloak (ADR-0003).");
        // Defining clientScopes in the import suppresses Keycloak's built-in scopes (incl. "basic" → no "sub" claim).
        Assert.False(realm.TryGetProperty("clientScopes", out _), "Do not define clientScopes in the realm import.");
        foreach (var user in realm.GetProperty("users").EnumerateArray())
        {
            Assert.False(user.TryGetProperty("realmRoles", out _));
            if (user.TryGetProperty("serviceAccountClientId", out _))
            {
                Assert.False(user.TryGetProperty("credentials", out _), "A service account has no password.");
                continue;
            }

            foreach (var cred in user.GetProperty("credentials").EnumerateArray())
            {
                Assert.Equal("${MFOP_DEV_USER_PASSWORD}", cred.GetProperty("value").GetString());
            }
        }
    }

    [Fact]
    public void Clients_are_public_pkce_without_password_grant_and_carry_api_audience()
    {
        var clients = Realm().GetProperty("clients").EnumerateArray().Where(c => c.GetProperty("clientId").GetString() != "mfop-backend").ToList();
        Assert.Equal(new[] { "mfop-mobile", "mfop-web" }, clients.Select(c => c.GetProperty("clientId").GetString()).Order());
        foreach (var c in clients)
        {
            Assert.True(c.GetProperty("publicClient").GetBoolean());
            Assert.False(c.GetProperty("directAccessGrantsEnabled").GetBoolean());
            Assert.False(c.GetProperty("implicitFlowEnabled").GetBoolean());
            Assert.Equal("S256", c.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
            var scopes = c.GetProperty("defaultClientScopes").EnumerateArray().Select(s => s.GetString()).ToList();
            Assert.Contains("basic", scopes); // provides the "sub" claim
            var audience = c.GetProperty("protocolMappers").EnumerateArray()
                .Single(m => m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper");
            Assert.Equal("mfop-api", audience.GetProperty("config").GetProperty("included.custom.audience").GetString());
        }
    }

    /// <summary>docs/lean-pilot/05 §5.6 step 4: the API's own confidential client, used only to disable users and end sessions.</summary>
    [Fact]
    public void Backend_client_is_a_service_account_with_user_management_only_and_no_committed_secret()
    {
        var realm = Realm();
        var backend = realm.GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("clientId").GetString() == "mfop-backend");
        Assert.False(backend.GetProperty("publicClient").GetBoolean());
        Assert.True(backend.GetProperty("serviceAccountsEnabled").GetBoolean());
        Assert.False(backend.GetProperty("standardFlowEnabled").GetBoolean());
        Assert.False(backend.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.False(backend.GetProperty("implicitFlowEnabled").GetBoolean());
        Assert.Equal("${MFOP_KEYCLOAK_BACKEND_SECRET}", backend.GetProperty("secret").GetString());

        var account = realm.GetProperty("users").EnumerateArray()
            .Single(u => u.TryGetProperty("serviceAccountClientId", out var id) && id.GetString() == "mfop-backend");
        var roles = account.GetProperty("clientRoles").GetProperty("realm-management").EnumerateArray().Select(r => r.GetString()).Order().ToList();
        Assert.Equal(new[] { "manage-users", "view-users" }, roles);
        Assert.Single(account.GetProperty("clientRoles").EnumerateObject());
    }
}
