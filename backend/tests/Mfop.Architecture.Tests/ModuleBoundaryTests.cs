using System.Reflection;
using NetArchTest.Rules;

namespace Mfop.Architecture.Tests;

/// <summary>Enforces ADR-0001 module boundaries.</summary>
public class ModuleBoundaryTests
{
    private static string Failing(TestResult result) =>
        string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []);

    private static readonly Assembly[] Modules =
    [
        typeof(Mfop.Access.AccessModule).Assembly,
        typeof(Mfop.Assets.AssetsModule).Assembly,
        typeof(Mfop.Faults.FaultsModule).Assembly,
        typeof(Mfop.WorkOrders.WorkOrdersModule).Assembly,
        typeof(Mfop.Evidence.EvidenceModule).Assembly,
        typeof(Mfop.Inventory.InventoryModule).Assembly,
        typeof(Mfop.Audit.AuditModule).Assembly,
    ];

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var m in Modules)
        {
            data.Add(m.GetName().Name!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Modules_only_use_other_modules_through_contracts(string moduleName)
    {
        var module = Modules.Single(m => m.GetName().Name == moduleName);
        foreach (var other in Modules.Where(m => m != module))
        {
            var otherName = other.GetName().Name!;
            var forbidden = new[] { $"{otherName}.Domain", $"{otherName}.Application", $"{otherName}.Infrastructure", $"{otherName}.Api" };
            var result = Types.InAssembly(module).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            Assert.True(result.IsSuccessful, $"{moduleName} depends on internals of {otherName}: {Failing(result)}");
        }
    }

    [Fact]
    public void Kernel_depends_on_no_module_or_framework_infrastructure()
    {
        var result = Types.InAssembly(typeof(Mfop.Kernel.ModuleInfo).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Mfop.Access", "Mfop.Assets", "Mfop.Faults", "Mfop.WorkOrders", "Mfop.Evidence", "Mfop.Inventory", "Mfop.Audit",
                "Mfop.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_layer_has_no_persistence_or_http_dependencies(string moduleName)
    {
        var module = Modules.Single(m => m.GetName().Name == moduleName);
        var result = Types.InAssembly(module)
            .That().ResideInNamespace($"{moduleName}.Domain")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();
        Assert.True(result.IsSuccessful, Failing(result));
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Every_module_declares_its_owned_schema(string moduleName)
    {
        var module = Modules.Single(m => m.GetName().Name == moduleName);
        var info = module.GetTypes()
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed && t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Select(t => t.GetField("Info")?.GetValue(null))
            .OfType<Mfop.Kernel.ModuleInfo>()
            .Single();
        Assert.Matches("^[a-z]+$", info.Schema);
    }
}
