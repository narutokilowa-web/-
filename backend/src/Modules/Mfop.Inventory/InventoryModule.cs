using Mfop.Infrastructure;
using Mfop.Inventory.Application;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Inventory;

/// <summary>
/// Inventory module (docs/lean-pilot/07): items and costs, locations, the append-only movement ledger and balances (M3), and
/// the M5 accountability documents: stock issues with technician acknowledgment/dispute, unused returns, core returns with
/// serial matching, offboarding returns, reconciliation and history.
/// Owns PostgreSQL schema "inventory". Other modules may only use types in the Mfop.Inventory.Contracts namespace.
/// </summary>
public static class InventoryModule
{
    public static readonly ModuleInfo Info = new("Inventory", "inventory");

    public static IServiceCollection AddInventoryModule(this IServiceCollection services, IConfiguration configuration, bool withBackgroundTimers = true)
    {
        services.AddModuleDbContext<InventoryDbContext>(Info.Schema);
        services.Configure<InventoryTimerOptions>(configuration.GetSection("Inventory"));
        services.AddScoped<InventoryService>();
        services.AddScoped<StockDocumentService>();
        services.AddScoped<InventoryQueries>();
        services.AddScoped<InventoryTimers>();
        services.AddScoped<IInventoryLedger>(sp => sp.GetRequiredService<InventoryQueries>());
        services.AddScoped<ISyncCommandHandler, AcknowledgeStockIssueHandler>();
        services.AddScoped<ISyncCommandHandler, DisputeStockIssueHandler>();
        services.AddScoped<ISyncCommandHandler, DeclareStockReturnHandler>();
        if (withBackgroundTimers)
        {
            services.AddHostedService<InventoryTimerWorker>();
        }

        return services;
    }
}
