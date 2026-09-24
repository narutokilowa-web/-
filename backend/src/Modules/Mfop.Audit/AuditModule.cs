using Mfop.Kernel;

namespace Mfop.Audit;

/// <summary>
/// Audit module. Skeleton only in M0; see docs/lean-pilot/11-milestones-and-dod.md for the milestone that fills it.
/// Owns PostgreSQL schema "audit". Other modules may only use types in the Mfop.Audit.Contracts namespace.
/// </summary>
public static class AuditModule
{
    public static readonly ModuleInfo Info = new("Audit", "audit");
}
