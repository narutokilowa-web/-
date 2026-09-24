using Mfop.Kernel.Auditing;

namespace Mfop.Assets.Domain;

/// <summary>
/// An approved snapshot of an asset's official attributes. Append-only (no UPDATE privilege + trigger).
/// snapshot_hash = SHA-256(canonical JSON of the snapshot); it is also recorded in the audit event, so the verifier
/// can detect a changed snapshot, a changed hash, or a changed asset row.
/// </summary>
public sealed class AssetVersion : IAuditable
{
    private AssetVersion() { }

    public AssetVersion(Guid id, Guid assetId, int versionNo, string snapshot, string snapshotHash, Guid approvedBy, DateTimeOffset approvedAt, string reason)
    {
        Id = id;
        AssetId = assetId;
        VersionNo = versionNo;
        Snapshot = snapshot;
        SnapshotHash = snapshotHash;
        ApprovedBy = approvedBy;
        ApprovedAt = approvedAt;
        Reason = reason;
    }

    public string AuditEntityType => "asset_version";

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public int VersionNo { get; private set; }
    public string Snapshot { get; private set; } = "{}";
    public string SnapshotHash { get; private set; } = "";
    public Guid ApprovedBy { get; private set; }
    public DateTimeOffset ApprovedAt { get; private set; }
    public string Reason { get; private set; } = "";
}
