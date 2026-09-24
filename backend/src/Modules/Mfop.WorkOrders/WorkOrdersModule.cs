using Mfop.Assets.Contracts;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Mfop.WorkOrders.Application;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.WorkOrders;

/// <summary>
/// WorkOrders module: work orders created from faults, assignments to technicians (M2); field execution and review (M3).
/// Owns PostgreSQL schema "workorders". Other modules may only use types in the Mfop.WorkOrders.Contracts namespace.
/// </summary>
public static class WorkOrdersModule
{
    public static readonly ModuleInfo Info = new("WorkOrders", "workorders");

    public static IServiceCollection AddWorkOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<WorkOrdersDbContext>(Info.Schema);
        services.Configure<WorkOrderOptions>(configuration.GetSection("WorkOrders"));
        services.AddScoped<WorkOrderService>();
        services.AddScoped<WorkOrderQueries>();
        services.AddScoped<IWorkOrderTasks>(sp => sp.GetRequiredService<WorkOrderQueries>());
        services.AddScoped<IAssetRelatedHistory, WorkOrderAssetHistory>();
        return services;
    }
}
