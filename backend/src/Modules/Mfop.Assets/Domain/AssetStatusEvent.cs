using Mfop.Kernel.Auditing;

namespace Mfop.Assets.Domain;

/// <summary>
/// One change of a street light's operational status and what caused it (docs/lean-pilot/04 §4.6). Append-only.
/// Operational status has no manual edit command: it changes only through these events.
/// </summary>
public sealed class AssetStatusEvent : IAuditable
{
    private AssetStatusEvent() { }

    public AssetStatusEvent(Guid id, Guid assetId, string transitionId, string? fromStatus, string toStatus, string causeType, Guid causeId, DateTimeOffset at)
    {
        Id = id;
        AssetId = assetId;
        TransitionId = transitionId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        CauseType = causeType;
        CauseId = causeId;
        At = at;
    }

    public string AuditEntityType => "asset_status_event";

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public string TransitionId { get; private set; } = string.Empty;
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = string.Empty;
    public string CauseType { get; private set; } = string.Empty;
    public Guid CauseId { get; private set; }
    public DateTimeOffset At { get; private set; }
}
