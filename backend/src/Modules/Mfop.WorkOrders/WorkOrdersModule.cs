using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Commands;
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
        services.AddScoped<WorkOrderRelatedEntities>();
        services.AddScoped<IWorkOrderTasks>(sp => sp.GetRequiredService<WorkOrderQueries>());
        services.AddScoped<IAssetRelatedHistory, WorkOrderAssetHistory>();
        services.AddScoped<WorkOrderReferenceData>();
        services.AddScoped<IWorkOrderReference>(sp => sp.GetRequiredService<WorkOrderReferenceData>());
        services.AddScoped<ReferenceDataLoader>();
        services.AddScoped<FieldExecutionService>();
        services.AddScoped<ReviewService>();
        services.AddScoped<IUserOffboardingHandler, WorkOrderOffboarding>();
        services.AddScoped<ISyncCommandHandler, ArriveOnSiteHandler>();
        services.AddScoped<ISyncCommandHandler, AddEvidenceHandler>();
        services.AddScoped<ISyncCommandHandler, RecordDiagnosisHandler>();
        services.AddScoped<ISyncCommandHandler, StartWorkHandler>();
        services.AddScoped<ISyncCommandHandler, ConsumeMaterialHandler>();
        services.AddScoped<ISyncCommandHandler, RecordChecklistHandler>();
        services.AddScoped<ISyncCommandHandler, DeclareCoreReturnHandler>();
        services.AddScoped<ISyncCommandHandler, SubmitWorkOrderHandler>();
        services.AddScoped<ISyncCommandHandler, ResubmitHandler>();
        return services;
    }
}
