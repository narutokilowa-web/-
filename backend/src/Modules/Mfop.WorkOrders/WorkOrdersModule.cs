using Mfop.Kernel;

namespace Mfop.WorkOrders;

/// <summary>
/// WorkOrders module. Skeleton only in M0; see docs/lean-pilot/11-milestones-and-dod.md for the milestone that fills it.
/// Owns PostgreSQL schema "workorders". Other modules may only use types in the Mfop.WorkOrders.Contracts namespace.
/// </summary>
public static class WorkOrdersModule
{
    public static readonly ModuleInfo Info = new("WorkOrders", "workorders");
}
