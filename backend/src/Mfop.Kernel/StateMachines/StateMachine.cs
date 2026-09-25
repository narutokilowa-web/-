using Mfop.Kernel.Results;

namespace Mfop.Kernel.StateMachines;

/// <param name="Id">Transition id as in docs/state-machines/*.yaml (e.g. "R5").</param>
/// <param name="From">Allowed source states; "_start" means creation.</param>
/// <param name="Actors">Role codes or relational actors (assignee, registrar, system).</param>
public sealed record Transition(string Id, IReadOnlyList<string> From, string Command, string To, IReadOnlyList<string> Actors);

/// <summary>
/// An explicit transition table. Domain code asks the machine whether a command is allowed in a state;
/// anything not listed is rejected with INVALID_TRANSITION. Tests assert every machine equals its YAML file exactly.
/// </summary>
public sealed class StateMachine
{
    public const string Start = "_start";

    public StateMachine(string name, IReadOnlyList<string> states, string initial, IReadOnlyList<string> terminal, IReadOnlyList<Transition> transitions)
    {
        Name = name;
        States = states;
        Initial = initial;
        Terminal = terminal;
        Transitions = transitions;

        var unknown = transitions.SelectMany(t => t.From.Append(t.To)).Where(s => s != Start && !states.Contains(s)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"{name}: unknown states {string.Join(", ", unknown)}");
        }
    }

    public string Name { get; }

    public IReadOnlyList<string> States { get; }

    public string Initial { get; }

    public IReadOnlyList<string> Terminal { get; }

    public IReadOnlyList<Transition> Transitions { get; }

    /// <summary>Returns the transition for (state, command) or an INVALID_TRANSITION error.</summary>
    public Result<Transition> Resolve(string state, string command)
    {
        var transition = Transitions.FirstOrDefault(t => t.Command == command && t.From.Contains(state));
        return transition is null ? Error.InvalidTransition(Name, state, command) : transition;
    }

    public Result<Transition> Create(string command) => Resolve(Start, command);
}
