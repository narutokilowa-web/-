using Mfop.Access.Contracts;
namespace Mfop.Access.Domain;

/// <summary>Role pairs one user may never hold together. Mirrors permissions.yaml `static_sod` (checked by a test).</summary>
public static class StaticSeparationOfDuties
{
    public static readonly IReadOnlyList<(string A, string B)> Pairs =
    [
        (RoleCodes.Registrar, RoleCodes.Approver),
        (RoleCodes.Warehouse, RoleCodes.Technician),
        (RoleCodes.Auditor, RoleCodes.Technician),
        (RoleCodes.Auditor, RoleCodes.Inspector),
        (RoleCodes.Auditor, RoleCodes.Supervisor),
        (RoleCodes.Auditor, RoleCodes.Registrar),
        (RoleCodes.Auditor, RoleCodes.Approver),
        (RoleCodes.Auditor, RoleCodes.Warehouse),
        (RoleCodes.Auditor, RoleCodes.Admin),
        (RoleCodes.Admin, RoleCodes.Technician),
        (RoleCodes.Admin, RoleCodes.Inspector),
        (RoleCodes.Admin, RoleCodes.Supervisor),
        (RoleCodes.Admin, RoleCodes.Registrar),
        (RoleCodes.Admin, RoleCodes.Approver),
        (RoleCodes.Admin, RoleCodes.Warehouse),
    ];

    public static string? ConflictWith(string role, IEnumerable<string> heldRoles)
    {
        foreach (var held in heldRoles)
        {
            if (Pairs.Any(p => (p.A == role && p.B == held) || (p.B == role && p.A == held)))
            {
                return held;
            }
        }

        return null;
    }
}
