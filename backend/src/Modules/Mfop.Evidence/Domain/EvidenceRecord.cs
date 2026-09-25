using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;
using Mfop.Kernel.StateMachines;

namespace Mfop.Evidence.Domain;

public static class EvidenceStates
{
    public const string Announced = "ANNOUNCED", Uploaded = "UPLOADED", Verified = "VERIFIED", HashMismatch = "HASH_MISMATCH", Duplicate = "DUPLICATE";
}

public static class EvidenceCommands
{
    public const string AddEvidence = nameof(AddEvidence);
    public const string UploadContent = nameof(UploadContent);
    public const string VerifyHashMatchAndUnique = nameof(VerifyHashMatchAndUnique);
    public const string VerifyHashMismatch = nameof(VerifyHashMismatch);
    public const string VerifyDuplicate = nameof(VerifyDuplicate);
}

public static class EvidenceKinds
{
    public const string Before = "BEFORE", After = "AFTER", Test = "TEST";

    /// <summary>Kinds used by work orders in DEMO 1 (registration/field-check/core-return kinds arrive with M5/M7).</summary>
    public static readonly IReadOnlySet<string> WorkOrder = new HashSet<string> { Before, After, Test };
}

/// <summary>Transition table. MUST equal docs/state-machines/evidence.yaml (StateMachineConformanceTests).</summary>
public static class EvidenceStateMachines
{
    public static readonly StateMachine Evidence = new(
        "Evidence",
        [EvidenceStates.Announced, EvidenceStates.Uploaded, EvidenceStates.Verified, EvidenceStates.HashMismatch, EvidenceStates.Duplicate],
        EvidenceStates.Announced,
        [EvidenceStates.Verified, EvidenceStates.HashMismatch, EvidenceStates.Duplicate],
        [
            new("E1", [StateMachine.Start], EvidenceCommands.AddEvidence, EvidenceStates.Announced, ["assignee"]),
            new("E2", [EvidenceStates.Announced], EvidenceCommands.UploadContent, EvidenceStates.Uploaded, ["assignee"]),
            new("E3", [EvidenceStates.Uploaded], EvidenceCommands.VerifyHashMatchAndUnique, EvidenceStates.Verified, ["system"]),
            new("E4", [EvidenceStates.Uploaded], EvidenceCommands.VerifyHashMismatch, EvidenceStates.HashMismatch, ["system"]),
            new("E5", [EvidenceStates.Uploaded], EvidenceCommands.VerifyDuplicate, EvidenceStates.Duplicate, ["system"]),
        ]);
}

/// <summary>
/// A photo captured in-app on the phone. The device hash is committed (announced) before the bytes arrive; the server
/// re-hashes the exact bytes it receives and a background verifier compares them. Evidence is never deleted:
/// a committed photo can only be superseded, with a reason, and stays visible to the reviewer and the auditor.
/// </summary>
public sealed class EvidenceRecord : IAuditable
{
    private EvidenceRecord() { }

    public EvidenceRecord(Guid id, string ownerType, Guid ownerId, string kind, string sha256Device, long sizeBytes, string mime,
        Guid capturedBy, Guid? deviceId, DateTimeOffset? capturedAt, DateTimeOffset announcedAt)
    {
        Id = id;
        OwnerType = ownerType;
        OwnerId = ownerId;
        Kind = kind;
        Sha256Device = sha256Device;
        SizeBytes = sizeBytes;
        Mime = mime;
        CapturedBy = capturedBy;
        DeviceId = deviceId;
        CapturedAt = capturedAt;
        AnnouncedAt = announcedAt;
        State = EvidenceStateMachines.Evidence.Create(EvidenceCommands.AddEvidence).Value.To;
    }

    public string AuditEntityType => "evidence";

    public Guid Id { get; private set; }

    /// <summary>WORK_ORDER in DEMO 1.</summary>
    public string OwnerType { get; private set; } = string.Empty;
    public Guid OwnerId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Sha256Device { get; private set; } = string.Empty;
    public string? Sha256Server { get; private set; }
    public long SizeBytes { get; private set; }
    public long? ReceivedBytes { get; private set; }
    public string Mime { get; private set; } = "image/jpeg";
    public Guid CapturedBy { get; private set; }
    public Guid? DeviceId { get; private set; }

    /// <summary>Phone clock at capture (trusted time arrives in M4).</summary>
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset AnnouncedAt { get; private set; }
    public DateTimeOffset? UploadedAt { get; private set; }
    public DateTimeOffset? VerifiedAt { get; private set; }
    public string State { get; private set; } = EvidenceStates.Announced;
    public string? StorageKey { get; private set; }
    public string? PreviewKey { get; private set; }
    public string? ThumbnailKey { get; private set; }
    public Guid? DuplicateOf { get; private set; }
    public Guid? SupersededBy { get; private set; }
    public string? SupersedeReason { get; private set; }
    public int Version { get; private set; }

    public bool IsSuperseded => SupersededBy is not null;

    public Result<Transition> MarkUploaded(string sha256Server, long receivedBytes, DateTimeOffset now)
    {
        var t = EvidenceStateMachines.Evidence.Resolve(State, EvidenceCommands.UploadContent);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        Sha256Server = sha256Server;
        ReceivedBytes = receivedBytes;
        UploadedAt = now;
        return t;
    }

    public Result<Transition> MarkVerified(string storageKey, string? previewKey, string? thumbnailKey, DateTimeOffset now)
    {
        var t = EvidenceStateMachines.Evidence.Resolve(State, EvidenceCommands.VerifyHashMatchAndUnique);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        StorageKey = storageKey;
        PreviewKey = previewKey;
        ThumbnailKey = thumbnailKey;
        VerifiedAt = now;
        return t;
    }

    public Result<Transition> MarkHashMismatch(DateTimeOffset now)
    {
        var t = EvidenceStateMachines.Evidence.Resolve(State, EvidenceCommands.VerifyHashMismatch);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        VerifiedAt = now;
        return t;
    }

    public Result<Transition> MarkDuplicate(Guid duplicateOf, DateTimeOffset now)
    {
        var t = EvidenceStateMachines.Evidence.Resolve(State, EvidenceCommands.VerifyDuplicate);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        DuplicateOf = duplicateOf;
        VerifiedAt = now;
        return t;
    }

    /// <summary>Not a state change: the photo stays stored and visible; it just no longer counts toward requirements.</summary>
    public void Supersede(Guid by, string reason)
    {
        SupersededBy = by;
        SupersedeReason = reason;
    }
}
