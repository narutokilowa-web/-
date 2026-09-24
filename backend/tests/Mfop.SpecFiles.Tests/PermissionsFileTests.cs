namespace Mfop.SpecFiles.Tests;

public class PermissionsFileTests
{
    private static PermissionsSpec Load() => Yaml.Load<PermissionsSpec>(RepoPaths.Path("docs", "lean-pilot", "permissions.yaml"));

    private static readonly string[] ExpectedRoles =
        ["TECHNICIAN", "INSPECTOR", "SUPERVISOR", "REGISTRAR", "APPROVER", "WAREHOUSE", "AUDITOR", "ADMIN"];

    [Fact]
    public void Roles_match_the_specification()
    {
        var spec = Load();
        Assert.Equal(ExpectedRoles.Order(), spec.Roles.Keys.Order());
        Assert.All(spec.Roles.Values, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(r.NameEn));
        });
    }

    [Fact]
    public void Static_sod_pairs_use_known_distinct_roles()
    {
        var spec = Load();
        Assert.All(spec.StaticSod, pair =>
        {
            Assert.Equal(2, pair.Count);
            Assert.NotEqual(pair[0], pair[1]);
            Assert.All(pair, r => Assert.Contains(r, spec.Roles.Keys));
        });
    }

    [Fact]
    public void Auditor_and_admin_are_incompatible_with_every_operational_role()
    {
        var spec = Load();
        var pairs = spec.StaticSod.Select(p => string.Join("|", p.Order())).ToHashSet();
        foreach (var separated in new[] { "AUDITOR", "ADMIN" })
        {
            foreach (var role in new[] { "TECHNICIAN", "INSPECTOR", "SUPERVISOR", "REGISTRAR", "APPROVER", "WAREHOUSE" })
            {
                Assert.Contains(string.Join("|", new[] { separated, role }.Order()), pairs);
            }
        }

        Assert.Contains(string.Join("|", new[] { "REGISTRAR", "APPROVER" }.Order()), pairs);
        Assert.Contains(string.Join("|", new[] { "WAREHOUSE", "TECHNICIAN" }.Order()), pairs);
    }

    [Fact]
    public void Every_action_references_known_roles_policies_and_milestones()
    {
        var spec = Load();
        Assert.NotEmpty(spec.Actions);
        foreach (var (name, action) in spec.Actions)
        {
            Assert.NotEmpty(action.Roles);
            Assert.All(action.Roles, r => Assert.True(spec.Roles.ContainsKey(r), $"{name}: unknown role {r}"));
            Assert.All(action.Policies, p => Assert.True(spec.Policies.ContainsKey(p), $"{name}: unknown policy {p}"));
            Assert.Matches("^M[0-9]$", action.Milestone);
        }
    }

    [Fact]
    public void No_delete_action_exists()
    {
        var spec = Load();
        Assert.DoesNotContain(spec.Actions.Keys, k => k.Contains("delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Separation_of_duties_policies_are_applied_where_required()
    {
        var spec = Load();
        Assert.Contains("approver_not_registrar", spec.Actions["registration.decide"].Policies);
        Assert.Contains("reviewer_not_executor", spec.Actions["workorder.review"].Policies);
        Assert.Contains("issuer_not_receiver", spec.Actions["stock.issue"].Policies);
        Assert.Contains("no_self_grant", spec.Actions["user.manage"].Policies);
    }
}
