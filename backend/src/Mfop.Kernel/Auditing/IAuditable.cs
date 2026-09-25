namespace Mfop.Kernel.Auditing;

/// <summary>
/// Every persisted operational entity implements this. The unit of work refuses to commit a changed entity
/// unless an audit record was staged for it (docs/adr/0007).
/// </summary>
public interface IAuditable
{
    /// <summary>Stable entity type name used in audit events (e.g. "street_light").</summary>
    string AuditEntityType { get; }

    Guid Id { get; }
}

/// <summary>Stages audit records for the current command. Old/new values are captured automatically at commit.</summary>
public interface IAuditTrail
{
    /// <param name="action">Stable action name, e.g. "registration.approved".</param>
    /// <param name="extra">Additional recorded facts (key → value). Values must be string, bool, integer or null.</param>
    /// <param name="actor">
    /// Who this event is attributed to, when it differs from the command's actor. Used only where one unit of work
    /// records the decisions of two people (e.g. a supervisor applying a technician's quarantined command, M4).
    /// </param>
    void Record(IAuditable entity, string action, string? reason = null, IReadOnlyDictionary<string, object?>? extra = null,
        Commands.ActorContext? actor = null);
}

/// <summary>
/// Technical records that are NOT business state and therefore carry no audit event of their own:
/// sync receipts (the applied command's own audit events carry its command_id) and device sync telemetry.
/// Allowed only on the types listed in Mfop.Architecture.Tests (AuditExemptAllowList); everything else must be IAuditable.
/// </summary>
public interface IAuditExempt;
