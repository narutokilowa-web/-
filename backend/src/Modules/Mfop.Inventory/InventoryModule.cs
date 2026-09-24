using Mfop.Kernel;

namespace Mfop.Inventory;

/// <summary>
/// Inventory module. Skeleton only in M0; see docs/lean-pilot/11-milestones-and-dod.md for the milestone that fills it.
/// Owns PostgreSQL schema "inventory". Other modules may only use types in the Mfop.Inventory.Contracts namespace.
/// </summary>
public static class InventoryModule
{
    public static readonly ModuleInfo Info = new("Inventory", "inventory");
}
