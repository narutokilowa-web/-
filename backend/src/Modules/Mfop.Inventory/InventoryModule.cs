using Mfop.Infrastructure;
using Mfop.Inventory.Application;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Inventory;

/// <summary>
/// Inventory module (DEMO 1 subset, docs/lean-pilot/07): items and costs, locations, the append-only movement ledger
/// (RECEIPT, one-step ISSUE, CONSUME, REVERSAL) and balances. Acknowledgment, returns and core returns are M5.
/// Owns PostgreSQL schema "inventory". Other modules may only use types in the Mfop.Inventory.Contracts namespace.
/// </summary>
public static class InventoryModule
{
    public static readonly ModuleInfo Info = new("Inventory", "inventory");

    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<InventoryDbContext>(Info.Schema);
        services.AddScoped<InventoryService>();
        services.AddScoped<InventoryQueries>();
        services.AddScoped<IInventoryLedger>(sp => sp.GetRequiredService<InventoryQueries>());
        return services;
    }
}
