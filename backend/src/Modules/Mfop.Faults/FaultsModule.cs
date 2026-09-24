using Mfop.Kernel;

namespace Mfop.Faults;

/// <summary>
/// Faults module. Skeleton only in M0; see docs/lean-pilot/11-milestones-and-dod.md for the milestone that fills it.
/// Owns PostgreSQL schema "faults". Other modules may only use types in the Mfop.Faults.Contracts namespace.
/// </summary>
public static class FaultsModule
{
    public static readonly ModuleInfo Info = new("Faults", "faults");
}
