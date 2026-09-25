namespace Mfop.Evidence.Contracts;

public static class EvidenceOwnerTypes
{
    public const string WorkOrder = "WORK_ORDER";
}

/// <param name="Sha256Device">Lower-case hex SHA-256 of the final JPEG bytes, computed on the phone.</param>
/// <param name="SupersedesEvidenceId">Optional: an earlier photo of the same owner that this one replaces (with a reason).</param>
public sealed record AnnounceEvidence(
    Guid EvidenceId, string OwnerType, Guid OwnerId, string Kind, string Sha256Device, long SizeBytes, string Mime,
    Guid CapturedBy, Guid? DeviceId, DateTimeOffset? CapturedAt, Guid? SupersedesEvidenceId, string? SupersedeReason);

public sealed record EvidenceDto(
    Guid Id, string OwnerType, Guid OwnerId, string Kind, string State, string Sha256Device, string? Sha256Server, long SizeBytes,
    Guid CapturedBy, Guid? DeviceId, DateTimeOffset? CapturedAt, DateTimeOffset AnnouncedAt, DateTimeOffset? UploadedAt, DateTimeOffset? VerifiedAt,
    bool HasPreview, Guid? DuplicateOf, Guid? SupersededBy, string? SupersedeReason);

/// <summary>Evidence metadata used by owners (work orders) inside their unit of work.</summary>
public interface IEvidenceRegistry
{
    /// <summary>E1: records the device hash before the bytes arrive (audited). Idempotency is the caller's command id.</summary>
    Task<Mfop.Kernel.Results.Result<Guid>> AnnounceAsync(AnnounceEvidence request, CancellationToken ct);

    Task<IReadOnlyList<EvidenceDto>> ForOwnersAsync(string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct);
}
