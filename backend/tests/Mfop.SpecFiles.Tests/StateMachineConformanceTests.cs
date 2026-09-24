using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Assets.Domain;
using Mfop.Kernel.StateMachines;

namespace Mfop.SpecFiles.Tests;

/// <summary>
/// The C# state machines must implement EXACTLY the YAML tables (docs/state-machines). Any behaviour change
/// therefore shows up as a YAML diff in review.
/// </summary>
public class StateMachineConformanceTests
{
    public static TheoryData<string> Implemented() => new(["registration.yaml", "asset-lifecycle.yaml", "asset-operational-status.yaml"]);

    private static StateMachine CodeFor(string file) => file switch
    {
        "registration.yaml" => AssetStateMachines.Registration,
        "asset-lifecycle.yaml" => AssetStateMachines.AssetLifecycle,
        "asset-operational-status.yaml" => AssetStateMachines.AssetOperationalStatus,
        _ => throw new ArgumentException(file),
    };

    [Theory]
    [MemberData(nameof(Implemented))]
    public void Code_machine_equals_yaml(string file)
    {
        var yaml = Yaml.Load<StateMachineSpec>(RepoPaths.Path("docs", "state-machines", file));
        var code = CodeFor(file);

        Assert.Equal(yaml.Machine, code.Name);
        Assert.Equal(yaml.States.Order(), code.States.Order());
        Assert.Equal(yaml.Initial, code.Initial);
        Assert.Equal(yaml.Terminal.Order(), code.Terminal.Order());
        Assert.Equal(yaml.Transitions.Select(t => t.Id).Order(), code.Transitions.Select(t => t.Id).Order());

        foreach (var y in yaml.Transitions)
        {
            var c = code.Transitions.Single(t => t.Id == y.Id);
            Assert.True(y.From.Order().SequenceEqual(c.From.Order()), $"{y.Id}: from differs");
            Assert.Equal(y.Command, c.Command);
            Assert.Equal(y.To, c.To);
            Assert.True(y.Actors.Order().SequenceEqual(c.Actors.Order()), $"{y.Id}: actors differ");
        }
    }

    [Theory]
    [MemberData(nameof(Implemented))]
    public void Every_unlisted_state_command_pair_is_rejected_and_every_listed_one_allowed(string file)
    {
        var yaml = Yaml.Load<StateMachineSpec>(RepoPaths.Path("docs", "state-machines", file));
        var code = CodeFor(file);
        var commands = yaml.Transitions.Select(t => t.Command).Distinct().ToList();

        foreach (var state in yaml.States.Append(StateMachine.Start))
        {
            foreach (var command in commands)
            {
                var listed = yaml.Transitions.FirstOrDefault(t => t.Command == command && t.From.Contains(state));
                var resolved = code.Resolve(state, command);
                if (listed is null)
                {
                    Assert.False(resolved.IsSuccess, $"{code.Name}: {command} must be rejected in {state}");
                    Assert.Equal("INVALID_TRANSITION", resolved.Error!.Code);
                }
                else
                {
                    Assert.True(resolved.IsSuccess, $"{code.Name}: {command} must be allowed in {state}");
                    Assert.Equal(listed.To, resolved.Value.To);
                }
            }
        }
    }

    [Fact]
    public void Terminal_states_accept_no_command()
    {
        foreach (var code in new[] { AssetStateMachines.Registration, AssetStateMachines.AssetLifecycle })
        {
            foreach (var terminal in code.Terminal)
            {
                Assert.All(code.Transitions.Select(t => t.Command).Distinct(), command => Assert.False(code.Resolve(terminal, command).IsSuccess));
            }
        }
    }

    [Fact]
    public void Static_separation_of_duties_in_code_equals_yaml()
    {
        var yaml = Yaml.Load<PermissionsSpec>(RepoPaths.Path("docs", "lean-pilot", "permissions.yaml"));
        static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";
        Assert.Equal(
            yaml.StaticSod.Select(p => Key(p[0], p[1])).Order(),
            StaticSeparationOfDuties.Pairs.Select(p => Key(p.A, p.B)).Order());
        Assert.Equal(yaml.Roles.Keys.Order(), RoleCodes.All.Order());
    }
}
