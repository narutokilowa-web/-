using System.Text.RegularExpressions;

namespace Mfop.Architecture.Tests;

/// <summary>
/// Complements ModuleBoundaryTests: `const` values are inlined by the compiler and leave no metadata reference,
/// so we also scan the source for references to another module's internals.
/// </summary>
public partial class SourceBoundaryTests
{
    private static readonly string[] Modules = ["Access", "Assets", "Audit", "Evidence", "Faults", "Inventory", "WorkOrders"];

    private static string ModulesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "backend", "src", "Modules");
    }

    public static TheoryData<string> ModuleNames() => new(Modules);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Source_only_references_other_modules_through_contracts(string module)
    {
        var violations = new List<string>();
        var dir = Path.Combine(ModulesRoot(), $"Mfop.{module}");
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in InternalReference().Matches(text))
            {
                if (m.Groups["module"].Value != module)
                {
                    violations.Add($"{Path.GetFileName(file)}: {m.Value}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [GeneratedRegex(@"Mfop\.(?<module>Access|Assets|Audit|Evidence|Faults|Inventory|WorkOrders)\.(Domain|Application|Infrastructure|Api)\b")]
    private static partial Regex InternalReference();
}
