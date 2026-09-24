using Mfop.Kernel;

namespace Mfop.Assets;

/// <summary>
/// Assets module. Skeleton only in M0; see docs/lean-pilot/11-milestones-and-dod.md for the milestone that fills it.
/// Owns PostgreSQL schema "assets". Other modules may only use types in the Mfop.Assets.Contracts namespace.
/// </summary>
public static class AssetsModule
{
    public static readonly ModuleInfo Info = new("Assets", "assets");
}
