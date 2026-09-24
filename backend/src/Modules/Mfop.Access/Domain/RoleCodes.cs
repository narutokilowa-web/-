namespace Mfop.Access.Domain;

/// <summary>Role codes. Must match docs/lean-pilot/permissions.yaml (checked by SpecFiles tests).</summary>
public static class RoleCodes
{
    public const string Technician = "TECHNICIAN";
    public const string Inspector = "INSPECTOR";
    public const string Supervisor = "SUPERVISOR";
    public const string Registrar = "REGISTRAR";
    public const string Approver = "APPROVER";
    public const string Warehouse = "WAREHOUSE";
    public const string Auditor = "AUDITOR";
    public const string Admin = "ADMIN";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Technician, Inspector, Supervisor, Registrar, Approver, Warehouse, Auditor, Admin,
    };
}
