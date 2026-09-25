using System.Net;
using System.Net.Http.Json;
using Mfop.Access.Api;
using Mfop.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using YamlDotNet.RepresentationModel;

namespace Mfop.Api.Tests;

/// <summary>
/// Generated from docs/lean-pilot/permissions.yaml: every endpoint × every role returns 403 FORBIDDEN exactly when
/// the role is not allowed. Record-level policies (maker ≠ checker, own records) are tested in RegistrationFlowTests.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class AuthorizationMatrixTests(DatabaseFixture db) : IAsyncLifetime
{
    private const string CurrentMilestone = "M5";

    private TestApp App { get; } = new(db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    private static Dictionary<string, (HashSet<string> Roles, string Milestone)> YamlActions()
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(RepoPaths.Path("docs", "lean-pilot", "permissions.yaml"))));
        var actions = (YamlMappingNode)((YamlMappingNode)yaml.Documents[0].RootNode).Children[new YamlScalarNode("actions")];
        return actions.Children.ToDictionary(
            kv => ((YamlScalarNode)kv.Key).Value!,
            kv =>
            {
                var map = (YamlMappingNode)kv.Value;
                var roles = ((YamlSequenceNode)map.Children[new YamlScalarNode("roles")]).Children.Select(r => ((YamlScalarNode)r).Value!).ToHashSet();
                return (roles, ((YamlScalarNode)map.Children[new YamlScalarNode("milestone")]).Value!);
            });
    }

    private IReadOnlyList<(RouteEndpoint Endpoint, string Method, string? Action)> ApiEndpoints()
    {
        var sources = App.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        return sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/", StringComparison.Ordinal) || e.RoutePattern.RawText!.StartsWith("/sync/", StringComparison.Ordinal))
            .Select(e => (e, e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single(), e.Metadata.GetMetadata<RequiredPermission>()?.Action))
            .ToList();
    }

    [Fact]
    public void Every_api_endpoint_declares_a_permission_or_is_an_identity_endpoint()
    {
        var unguarded = ApiEndpoints()
            .Where(x => x.Action is null && x.Endpoint.Metadata.GetMetadata<AnyAuthenticatedActor>() is null)
            .Select(x => $"{x.Method} {x.Endpoint.RoutePattern.RawText}");
        Assert.Empty(unguarded);
    }

    [Fact]
    public void Code_matrix_equals_permissions_yaml()
    {
        var yaml = YamlActions();
        Assert.Equal(yaml.Keys.Order(), PermissionMatrix.Actions.Keys.Order());
        foreach (var (action, spec) in yaml)
        {
            Assert.True(spec.Roles.SetEquals(PermissionMatrix.Actions[action]), $"roles differ for {action}");
        }
    }

    [Fact]
    public async Task Every_action_promised_up_to_this_milestone_has_an_endpoint_and_no_endpoint_is_early()
    {
        var yaml = YamlActions();
        // Phone commands are authorized per command inside POST /sync/v1/push, so a sync handler's permission counts as used.
        await using var scope = App.Services.CreateAsyncScope();
        var used = ApiEndpoints().Where(x => x.Action is not null).Select(x => x.Action!)
            .Concat(scope.ServiceProvider.GetServices<Mfop.Kernel.Commands.ISyncCommandHandler>().Select(h => h.Permission))
            .ToHashSet();
        var promised = yaml.Where(a => string.CompareOrdinal(a.Value.Milestone, CurrentMilestone) <= 0 && a.Key != "me.read").Select(a => a.Key).ToHashSet();
        Assert.Empty(promised.Except(used));
        Assert.DoesNotContain(used, a => string.CompareOrdinal(yaml[a].Milestone, CurrentMilestone) > 0);
    }

    public static TheoryData<string> Roles() => new(DevUsers.ByRole.Keys);

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Each_role_gets_403_exactly_where_the_matrix_denies_it(string role)
    {
        using var client = App.ClientFor(DevUsers.ByRole[role]);
        var failures = new List<string>();
        foreach (var (endpoint, method, action) in ApiEndpoints().Where(x => x.Action is not null))
        {
            var url = endpoint.RoutePattern.RawText!
                .Replace("{id:guid}", Guid.NewGuid().ToString())
                .Replace("{movementId:guid}", Guid.NewGuid().ToString())
                .Replace("{token}", "00000000.00");
            using var response = method switch
            {
                "GET" => await client.GetAsync(url + "?assetId=" + Guid.NewGuid()),
                "PUT" => await client.PutAsync(url, new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0])),
                _ => await client.PostAsJsonAsync(url, SampleBody(url)),
            };

            var allowed = PermissionMatrix.Allows(action!, [role]);
            var forbiddenByRole = response.StatusCode == HttpStatusCode.Forbidden && await response.ProblemCodeAsync() == ErrorCodesHttp.Forbidden;
            if (allowed == forbiddenByRole)
            {
                failures.Add($"{role} {method} {url} ({action}): expected {(allowed ? "not FORBIDDEN" : "403 FORBIDDEN")}, got {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);
    }

    private static object SampleBody(string url) =>
        url.EndsWith("/registrations", StringComparison.Ordinal) || url.EndsWith("/update", StringComparison.Ordinal)
            ? ApiHelpers.SampleRegistration()
            : new { note = "matrix-test", reason = "matrix-test" };
}
