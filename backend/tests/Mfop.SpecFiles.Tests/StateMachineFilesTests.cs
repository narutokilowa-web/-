namespace Mfop.SpecFiles.Tests;

/// <summary>Validates docs/state-machines/*.yaml against the rules in docs/state-machines/_schema.md.</summary>
public class StateMachineFilesTests
{
    private static readonly string[] RelationalActors = ["assignee", "registrar", "system"];

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(RepoPaths.Path("docs", "state-machines"), "*.yaml").Order())
        {
            data.Add(System.IO.Path.GetFileName(f));
        }

        return data;
    }

    private static StateMachineSpec Load(string file) => Yaml.Load<StateMachineSpec>(RepoPaths.Path("docs", "state-machines", file));

    [Fact]
    public void Demo1_machines_are_all_present()
    {
        var machines = Directory.GetFiles(RepoPaths.Path("docs", "state-machines"), "*.yaml")
            .Select(f => Yaml.Load<StateMachineSpec>(f).Machine)
            .ToHashSet();

        string[] required = ["Device", "Registration", "AssetLifecycle", "AssetOperationalStatus", "Fault",
            "WorkOrder", "Assignment", "Evidence", "StockLedger"];
        Assert.Empty(required.Except(machines));
    }

    [Fact]
    public void M4_machines_are_all_present()
    {
        var machines = Directory.GetFiles(RepoPaths.Path("docs", "state-machines"), "*.yaml")
            .Select(f => Yaml.Load<StateMachineSpec>(f).Machine)
            .ToHashSet();

        string[] required = ["User", "SyncQuarantine"];
        Assert.Empty(required.Except(machines));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Header_is_complete(string file)
    {
        var sm = Load(file);
        Assert.False(string.IsNullOrWhiteSpace(sm.Machine));
        Assert.Contains(sm.Scope, new[] { "demo1", "m4", "deferred" });
        Assert.False(string.IsNullOrWhiteSpace(sm.SpecRef));
        Assert.NotEmpty(sm.States);
        Assert.NotEmpty(sm.Transitions);
        Assert.Equal(sm.States.Count, sm.States.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Transition_ids_are_unique(string file)
    {
        var sm = Load(file);
        var dupes = sm.Transitions.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(dupes);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void All_referenced_states_are_declared(string file)
    {
        var sm = Load(file);
        var states = sm.States.ToHashSet();
        Assert.Contains(sm.Initial, states);
        Assert.All(sm.Terminal, s => Assert.Contains(s, states));
        foreach (var t in sm.Transitions)
        {
            Assert.NotEmpty(t.From);
            Assert.All(t.From, s => Assert.True(s == "_start" || states.Contains(s), $"{t.Id}: unknown from-state {s}"));
            Assert.True(states.Contains(t.To), $"{t.Id}: unknown to-state {t.To}");
            Assert.False(string.IsNullOrWhiteSpace(t.Command), $"{t.Id}: command missing");
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Initial_state_is_entered_by_a_start_transition(string file)
    {
        var sm = Load(file);
        Assert.Contains(sm.Transitions, t => t.From.Contains("_start") && t.To == sm.Initial);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Terminal_states_have_no_outgoing_transitions(string file)
    {
        var sm = Load(file);
        var leaving = sm.Transitions.Where(t => t.From.Intersect(sm.Terminal).Any()).Select(t => t.Id);
        Assert.Empty(leaving);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Non_terminal_states_have_an_outgoing_transition(string file)
    {
        var sm = Load(file);
        var dead = sm.States.Except(sm.Terminal).Except(sm.DeferredExits.Keys).Where(s => !sm.Transitions.Any(t => t.From.Contains(s)));
        Assert.Empty(dead);
        Assert.All(sm.DeferredExits, d =>
        {
            Assert.Contains(d.Key, sm.States);
            Assert.DoesNotContain(d.Key, sm.Terminal);
            Assert.False(string.IsNullOrWhiteSpace(d.Value), $"deferred_exits.{d.Key} needs a reference");
        });
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_state_is_reachable_from_start(string file)
    {
        var sm = Load(file);
        var reached = new HashSet<string> { "_start" };
        bool changed;
        do
        {
            changed = false;
            foreach (var t in sm.Transitions.Where(t => t.From.Any(reached.Contains)))
            {
                changed |= reached.Add(t.To);
            }
        }
        while (changed);

        Assert.All(sm.States, s => Assert.Contains(s, reached));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Actors_and_policies_are_known(string file)
    {
        var sm = Load(file);
        var perms = Yaml.Load<PermissionsSpec>(RepoPaths.Path("docs", "lean-pilot", "permissions.yaml"));
        foreach (var t in sm.Transitions)
        {
            var actors = t.Actors.ToList();
            Assert.NotEmpty(actors);
            Assert.All(actors, a => Assert.True(perms.Roles.ContainsKey(a) || RelationalActors.Contains(a), $"{t.Id}: unknown actor {a}"));
            Assert.All(t.Policies, p => Assert.True(perms.Policies.ContainsKey(p), $"{t.Id}: unknown policy {p}"));
        }
    }
}
