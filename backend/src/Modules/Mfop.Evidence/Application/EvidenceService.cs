using System.Security.Cryptography;
using Mfop.Evidence.Contracts;
using Mfop.Evidence.Domain;
using Mfop.Evidence.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Infrastructure.Storage;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mfop.Evidence.Application;

public sealed record UploadResult(Guid EvidenceId, string State, string? Sha256Server);

public sealed record EvidenceFile(Stream Content, string ContentType);

/// <summary>
/// Evidence pipeline (docs/lean-pilot/06 §6.2): announce (hash committed first) → stream upload with a server hash →
/// background verification. Every state change is audited.
/// </summary>
public sealed class EvidenceService(
    EvidenceDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IObjectStore store, IOptions<EvidenceOptions> options)
    : IEvidenceRegistry
{
    public const string NotOwner = "EVIDENCE_NOT_OWNER";

    public async Task<Result<Guid>> AnnounceAsync(AnnounceEvidence request, CancellationToken ct)
    {
        if (request.EvidenceId == Guid.Empty) return Error.Validation("INVALID_EVIDENCE", "evidenceId is required.");
        if (request.Mime != "image/jpeg") return Error.Validation("EVIDENCE_NOT_JPEG", "Only JPEG photos are accepted in the pilot.");
        if (request.Sha256Device is not { Length: 64 } || !request.Sha256Device.All(Uri.IsHexDigit))
        {
            return Error.Validation("INVALID_HASH", "sha256 must be 64 hexadecimal characters.");
        }

        if (request.SizeBytes <= 0 || request.SizeBytes > options.Value.MaxBytes) return Error.Validation("EVIDENCE_TOO_LARGE", "The photo is empty or too large.");
        if (await db.Evidence.AnyAsync(e => e.Id == request.EvidenceId, ct)) return Error.Conflict("EVIDENCE_EXISTS", "This evidence id was already used.");

        var evidence = new EvidenceRecord(request.EvidenceId, request.OwnerType, request.OwnerId, request.Kind, request.Sha256Device.ToLowerInvariant(),
            request.SizeBytes, request.Mime, request.CapturedBy, request.DeviceId,
            request.CapturedAt is { } at ? ClockExtensions.TruncateToMicros(at) : null, clock.UtcNowMicros());
        db.Evidence.Add(evidence);
        audit.Record(evidence, "evidence.announced");

        if (request.SupersedesEvidenceId is { } previousId)
        {
            if (string.IsNullOrWhiteSpace(request.SupersedeReason)) return Error.Validation("REASON_REQUIRED", "A reason is required to replace a photo.");
            var previous = await db.Evidence.SingleOrDefaultAsync(e => e.Id == previousId, ct);
            if (previous is null || previous.OwnerType != request.OwnerType || previous.OwnerId != request.OwnerId)
            {
                return Error.NotFound("EVIDENCE_NOT_FOUND", "The photo to replace does not belong to this work order.");
            }

            if (previous.IsSuperseded) return Error.Conflict("EVIDENCE_ALREADY_SUPERSEDED", "This photo was already replaced.");
            previous.Supersede(evidence.Id, request.SupersedeReason.Trim());
            audit.Record(previous, "evidence.superseded", request.SupersedeReason.Trim());
        }

        return evidence.Id;
    }

    public async Task<IReadOnlyList<EvidenceDto>> ForOwnersAsync(string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct) =>
        (await db.Evidence.AsNoTracking().Where(e => e.OwnerType == ownerType && ownerIds.Contains(e.OwnerId)).OrderBy(e => e.AnnouncedAt).ThenBy(e => e.Id).ToListAsync(ct))
        .Select(ToDto).ToList();

    /// <summary>
    /// E2: streams the bytes to the quarantine bucket while computing the server SHA-256. Only the person who captured the
    /// photo may upload it. A repeated upload after the photo left ANNOUNCED changes nothing and reports the current state.
    /// </summary>
    public async Task<Result<UploadResult>> UploadAsync(ActorContext actor, Guid evidenceId, Stream body, long? contentLength, CancellationToken ct)
    {
        var evidence = await db.Evidence.AsNoTracking().SingleOrDefaultAsync(e => e.Id == evidenceId, ct);
        if (evidence is null) return Error.NotFound("EVIDENCE_NOT_FOUND", "Announce the photo (AddEvidence) before uploading it.");
        if (evidence.CapturedBy != actor.UserId) return Error.Forbidden(NotOwner, "Only the person who took the photo can upload it.");
        if (evidence.State != EvidenceStates.Announced) return new UploadResult(evidence.Id, evidence.State, evidence.Sha256Server);
        if (contentLength is > 0 && contentLength > options.Value.MaxBytes) return Error.Validation("EVIDENCE_TOO_LARGE", "The photo is too large.");

        var key = IncomingKey(evidence.Id);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var guard = new HashingLimitStream(body, sha, options.Value.MaxBytes);
        try
        {
            await store.PutAsync(Buckets.EvidenceIncoming, key, guard, ct);
        }
        catch (EvidenceTooLargeException)
        {
            await store.DeleteIncomingAsync(key, ct);
            return Error.Validation("EVIDENCE_TOO_LARGE", "The photo is too large.");
        }

        if (!guard.StartsWithJpegMagic)
        {
            await store.DeleteIncomingAsync(key, ct);
            return Error.Validation("EVIDENCE_NOT_JPEG", "The uploaded bytes are not a JPEG image.");
        }

        var serverHash = Convert.ToHexStringLower(sha.GetHashAndReset());
        var received = guard.BytesRead;
        var command = CommandEnvelope.For(EvidenceCommands.UploadContent, actor);
        return await uow.ExecuteAsync<UploadResult>(command, async innerCt =>
        {
            var tracked = await db.Evidence.SingleAsync(e => e.Id == evidenceId, innerCt);
            var t = tracked.MarkUploaded(serverHash, received, clock.UtcNowMicros());
            if (!t.IsSuccess) return new UploadResult(tracked.Id, tracked.State, tracked.Sha256Server); // concurrent duplicate upload
            audit.Record(tracked, "evidence.uploaded");
            return new UploadResult(tracked.Id, tracked.State, serverHash);
        }, ct);
    }

    /// <summary>Derived image (preview or thumbnail, EXIF stripped). TECHNICIAN may see only photos they took (own_records_only).</summary>
    public async Task<Result<EvidenceFile>> DerivedAsync(ActorContext actor, Guid evidenceId, bool thumbnail, CancellationToken ct)
    {
        var evidence = await db.Evidence.AsNoTracking().SingleOrDefaultAsync(e => e.Id == evidenceId, ct);
        if (evidence is null) return Error.NotFound("EVIDENCE_NOT_FOUND", "Evidence not found.");
        var privileged = actor.HasRole("SUPERVISOR") || actor.HasRole("AUDITOR") || actor.HasRole("APPROVER");
        if (!privileged && evidence.CapturedBy != actor.UserId) return Error.Forbidden(NotOwner, "You can only see photos you took.");
        var key = thumbnail ? evidence.ThumbnailKey : evidence.PreviewKey;
        if (evidence.State != EvidenceStates.Verified || key is null) return Error.NotFound("PREVIEW_NOT_READY", "No preview yet: the photo is not verified.");
        var stream = await store.OpenReadAsync(Buckets.EvidenceDerived, key, ct);
        return stream is null ? Error.NotFound("PREVIEW_MISSING", "The preview file is missing.") : new EvidenceFile(stream, "image/jpeg");
    }

    /// <summary>The original bytes (with EXIF). AUDITOR only; every access is an audit event (06 §6.1).</summary>
    public async Task<Result<EvidenceFile>> OriginalAsync(CommandEnvelope cmd, Guid evidenceId, CancellationToken ct)
    {
        var found = await uow.ExecuteAsync<EvidenceRecord>(cmd, async innerCt =>
        {
            var evidence = await db.Evidence.SingleOrDefaultAsync(e => e.Id == evidenceId, innerCt);
            if (evidence is null) return Error.NotFound("EVIDENCE_NOT_FOUND", "Evidence not found.");
            audit.Record(evidence, "evidence.original_viewed");
            return evidence;
        }, ct);
        if (!found.IsSuccess) return found.Error!;
        var e = found.Value;
        var stream = e.StorageKey is { } k
            ? await store.OpenReadAsync(Buckets.Evidence, k, ct)
            : await store.OpenReadAsync(Buckets.EvidenceIncoming, IncomingKey(e.Id), ct); // not verified (e.g. HASH_MISMATCH): the received bytes
        return stream is null ? Error.NotFound("ORIGINAL_MISSING", "No stored bytes for this evidence.") : new EvidenceFile(stream, "image/jpeg");
    }

    internal static string IncomingKey(Guid id) => $"{id:D}.jpg";

    internal static EvidenceDto ToDto(EvidenceRecord e) => new(
        e.Id, e.OwnerType, e.OwnerId, e.Kind, e.State, e.Sha256Device, e.Sha256Server, e.SizeBytes, e.CapturedBy, e.DeviceId, e.CapturedAt, e.AnnouncedAt,
        e.UploadedAt, e.VerifiedAt, e.PreviewKey is not null, e.DuplicateOf, e.SupersededBy, e.SupersedeReason);
}

internal sealed class EvidenceTooLargeException : Exception;

/// <summary>Read-through stream that hashes every byte, counts them, enforces the size limit and checks the JPEG magic bytes.</summary>
internal sealed class HashingLimitStream(Stream inner, IncrementalHash hash, long maxBytes) : Stream
{
    private readonly byte[] _head = new byte[3];

    public long BytesRead { get; private set; }

    public bool StartsWithJpegMagic => BytesRead >= 3 && _head[0] == 0xFF && _head[1] == 0xD8 && _head[2] == 0xFF;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Track(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        return Track(buffer.Span[..n]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Track(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length && BytesRead + i < 3; i++)
        {
            _head[BytesRead + i] = data[i];
        }

        BytesRead += data.Length;
        if (BytesRead > maxBytes)
        {
            throw new EvidenceTooLargeException();
        }

        hash.AppendData(data);
        return data.Length;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
