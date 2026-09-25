using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mfop.SpecFiles.Tests;

internal sealed class StateMachineSpec
{
    public string Machine { get; set; } = "";
    public string Scope { get; set; } = "";
    public string SpecRef { get; set; } = "";
    public List<string> States { get; set; } = [];
    public string Initial { get; set; } = "";
    public List<string> Terminal { get; set; } = [];
    public Dictionary<string, string> DeferredExits { get; set; } = [];
    public List<TransitionSpec> Transitions { get; set; } = [];
    public List<InStateCommandSpec> InStateCommands { get; set; } = [];
}

internal sealed class InStateCommandSpec
{
    public string Command { get; set; } = "";
    public List<string> In { get; set; } = [];
    public string Actor { get; set; } = "";
}

internal sealed class TransitionSpec
{
    public string Id { get; set; } = "";
    public List<string> From { get; set; } = [];
    public string Command { get; set; } = "";
    public string To { get; set; } = "";
    public object? Actor { get; set; }
    public List<string> Policies { get; set; } = [];
    public List<string> Requires { get; set; } = [];
    public List<string> Effects { get; set; } = [];

    public IEnumerable<string> Actors => Actor switch
    {
        string s => [s],
        IEnumerable<object> list => list.Select(x => x.ToString()!),
        _ => [],
    };
}

internal sealed class PermissionsSpec
{
    public int Version { get; set; }
    public Dictionary<string, RoleSpec> Roles { get; set; } = [];
    public List<List<string>> StaticSod { get; set; } = [];
    public Dictionary<string, string> Policies { get; set; } = [];
    public Dictionary<string, ActionSpec> Actions { get; set; } = [];
}

internal sealed class RoleSpec
{
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
}

internal sealed class ActionSpec
{
    public List<string> Roles { get; set; } = [];
    public List<string> Policies { get; set; } = [];
    public string Milestone { get; set; } = "";
}

internal static class Yaml
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static T Load<T>(string path) => Deserializer.Deserialize<T>(File.ReadAllText(path));
}
