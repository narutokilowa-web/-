using System.Security.Cryptography;
using Mfop.Evidence.Domain;
using Mfop.Evidence.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Infrastructure.Storage;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Rules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mfop.Evidence.Application;

/// <summary>
/// E3/E4/E5, actor SYSTEM, one unit of work per photo:
///   server hash ≠ device hash → HASH_MISMATCH + CRITICAL flag (bytes changed after capture, or corruption);
///   same bytes as an already VERIFIED photo → DUPLICATE + CRITICAL flag (photo reuse);
///   otherwise → original copied to the evidence store, preview and thumbnail derived without EXIF → VERIFIED.
/// </summary>
public sealed class EvidenceVerifier(
    EvidenceDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IObjectStore store, IFlags flags,
    IOptions<EvidenceOptions> options, ILogger<EvidenceVerifier> log)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var pending = await db.Evidence.AsNoTracking().Where(e => e.State == EvidenceStates.Uploaded)
            .OrderBy(e => e.UploadedAt).ThenBy(e => e.Id).Select(e => e.Id).Take(50).ToListAsync(ct);
        foreach (var id in pending)
        {
            await VerifyAsync(id, ct);
        }

        return pending.Count;
    }

    private async Task VerifyAsync(Guid id, CancellationToken ct)
    {
        var cmd = CommandEnvelope.For("VerifyEvidence", ActorContext.System);
        var result = await uow.ExecuteAsync<string>(cmd, async innerCt =>
        {
            var evidence = await db.Evidence.SingleAsync(e => e.Id == id, innerCt);
            if (evidence.State != EvidenceStates.Uploaded) return evidence.State;
            var now = clock.UtcNowMicros();

            byte[] bytes;
            await using (var incoming = await store.OpenReadAsync(Buckets.EvidenceIncoming, EvidenceService.IncomingKey(id), innerCt))
            {
                if (incoming is null)
                {
                    log.LogWarning("Evidence {EvidenceId} has no incoming file; left UPLOADED", id);
                    return evidence.State;
                }

                using var ms = new MemoryStream();
                await incoming.CopyToAsync(ms, innerCt);
                bytes = ms.ToArray();
            }

            // Re-hash what is stored now: the bytes are compared again with the announced device hash.
            var stored = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (stored != evidence.Sha256Device || evidence.Sha256Server != evidence.Sha256Device)
            {
                evidence.MarkHashMismatch(now);
                audit.Record(evidence, "evidence.hash_mismatch");
                await flags.RaiseAsync(FlagCodes.EvidenceHashMismatch, "CRITICAL", SubjectType(evidence), evidence.OwnerId, new Dictionary<string, object?>
                {
                    ["evidence_id"] = evidence.Id.ToString("D"),
                    ["kind"] = evidence.Kind,
                    ["sha256_device"] = evidence.Sha256Device,
                    ["sha256_server"] = evidence.Sha256Server,
                }, innerCt);
                return evidence.State;
            }

            var original = await db.Evidence.AsNoTracking()
                .Where(e => e.Id != id && e.State == EvidenceStates.Verified && e.Sha256Server == evidence.Sha256Server)
                .OrderBy(e => e.VerifiedAt).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(innerCt);
            if (original is { } firstUse)
            {
                evidence.MarkDuplicate(firstUse, now);
                audit.Record(evidence, "evidence.duplicate");
                await flags.RaiseAsync(FlagCodes.EvidenceDuplicate, "CRITICAL", SubjectType(evidence), evidence.OwnerId, new Dictionary<string, object?>
                {
                    ["evidence_id"] = evidence.Id.ToString("D"),
                    ["kind"] = evidence.Kind,
                    ["duplicate_of"] = firstUse.ToString("D"),
                    ["sha256"] = evidence.Sha256Server,
                }, innerCt);
                return evidence.State;
            }

            var storageKey = $"{id:D}.jpg";
            await store.PutAsync(Buckets.Evidence, storageKey, new MemoryStream(bytes), innerCt);
            string? previewKey = null, thumbKey = null;
            var preview = PreviewRenderer.Render(bytes, options.Value.PreviewMaxPixels);
            var thumb = PreviewRenderer.Render(bytes, options.Value.ThumbnailMaxPixels);
            if (preview is not null && thumb is not null)
            {
                previewKey = $"{id:D}-preview.jpg";
                thumbKey = $"{id:D}-thumb.jpg";
                await store.PutAsync(Buckets.EvidenceDerived, previewKey, new MemoryStream(preview), innerCt);
                await store.PutAsync(Buckets.EvidenceDerived, thumbKey, new MemoryStream(thumb), innerCt);
            }

            evidence.MarkVerified(storageKey, previewKey, thumbKey, now);
            audit.Record(evidence, "evidence.verified");
            return evidence.State;
        }, ct);

        if (result.IsSuccess && result.Value == EvidenceStates.Verified)
        {
            await store.DeleteIncomingAsync(EvidenceService.IncomingKey(id), ct); // after commit: the original is now in the evidence store
        }
    }

    private static string SubjectType(EvidenceRecord e) => e.OwnerType == "WORK_ORDER" ? "work_order" : e.OwnerType.ToLowerInvariant();
}

/// <summary>DEMO 1: evidence verification runs as a hosted service inside the API process (a separate worker in M8).</summary>
public sealed class EvidenceVerificationWorker(IServiceScopeFactory scopes, IOptions<EvidenceOptions> options, ILogger<EvidenceVerificationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunBackgroundVerifier)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(0.2, options.Value.VerifyIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EvidenceVerifier>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Evidence verification pass failed; retrying");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
