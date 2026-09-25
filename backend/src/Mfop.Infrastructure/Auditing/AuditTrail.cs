using Mfop.Kernel.Auditing;

namespace Mfop.Infrastructure.Auditing;

/// <summary>Scoped collector of audit records staged by command handlers.</summary>
public sealed class AuditTrail : IAuditTrail
{
    private readonly List<StagedAuditRecord> _records = [];

    public IReadOnlyList<StagedAuditRecord> Records => _records;

    public void Record(IAuditable entity, string action, string? reason = null, IReadOnlyDictionary<string, object?>? extra = null,
        Kernel.Commands.ActorContext? actor = null) =>
        _records.Add(new StagedAuditRecord(entity, action, reason, extra, actor));

    internal void Clear() => _records.Clear();
}

public sealed record StagedAuditRecord(IAuditable Entity, string Action, string? Reason, IReadOnlyDictionary<string, object?>? Extra,
    Kernel.Commands.ActorContext? Actor = null);
