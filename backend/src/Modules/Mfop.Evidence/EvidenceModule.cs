using Mfop.Kernel;

namespace Mfop.Evidence;

/// <summary>
/// Evidence module. Skeleton only in M0; see docs/lean-pilot/11-milestones-and-dod.md for the milestone that fills it.
/// Owns PostgreSQL schema "evidence". Other modules may only use types in the Mfop.Evidence.Contracts namespace.
/// </summary>
public static class EvidenceModule
{
    public static readonly ModuleInfo Info = new("Evidence", "evidence");
}
