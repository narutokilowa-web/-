using System.Text;
using System.Text.Json.Nodes;
using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Json;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

/// <summary>
/// Offline grants (docs/lean-pilot/05 §5.5, 04 §4.13): issued online to an ACTIVE user on an ACTIVE device, signed, with the
/// lifetime read from <c>policy.offline_grace_hours</c>; the previous grant of the device is revoked. The server-side check of
/// every pushed command is <see cref="CheckAsync"/>.
/// </summary>
public sealed class GrantService(AccessDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, GrantSigner signer, PolicyService policies)
    : IOfflineGrants
{
    public Task<Result<GrantDto>> IssueAsync(CommandEnvelope cmd, CancellationToken ct) =>
        uow.ExecuteAsync<GrantDto>(cmd, async innerCt =>
        {
            if (cmd.Actor.DeviceId is not { } deviceId || cmd.Actor.UserId is not { } userId)
            {
                return Error.Forbidden(DeviceErrorCodes.DeviceRequired, "A grant is issued only to an approved device.");
            }

            var now = clock.UtcNowMicros();
            var hours = await policies.IntAsync(PolicyKeys.OfflineGraceHours, innerCt);
            await RevokeOpenGrantsAsync(d => d.DeviceId == deviceId, GrantRevokeReasons.NewGrant, userId, now, innerCt);

            var id = Ids.New();
            var roles = cmd.Actor.Roles.Order(StringComparer.Ordinal).ToArray();
            var expires = now.AddHours(hours);
            var payload = CanonicalJson.Serialize(new JsonObject
            {
                ["grant_id"] = id.ToString("D"),
                ["user_id"] = userId.ToString("D"),
                ["device_id"] = deviceId.ToString("D"),
                ["roles"] = new JsonArray(roles.Select(r => (JsonNode)r).ToArray()),
                ["issued_at"] = Iso(now),
                ["expires_at"] = Iso(expires),
                ["policy_hours"] = hours,
                ["kid"] = signer.KeyId,
            });
            var signature = signer.Sign(payload);
            var grant = new OfflineGrant(id, userId, deviceId, roles, now, hours, signer.KeyId, signature);
            db.OfflineGrants.Add(grant);
            audit.Record(grant, "offline_grant.issued");
            return new GrantDto(id, userId, deviceId, roles, now, expires, hours, signer.KeyId, Encoding.UTF8.GetString(payload), signature,
                signer.PublicKey, await policies.ClientPolicyAsync(innerCt), now);
        }, ct);

    /// <summary>Revokes every grant matching <paramref name="filter"/> that is not yet revoked or expired (inside the caller's unit of work).</summary>
    public async Task RevokeOpenGrantsAsync(System.Linq.Expressions.Expression<Func<OfflineGrant, bool>> filter, string reason, Guid? actorId,
        DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.OfflineGrants.Where(filter).Where(g => g.ExpiresAt > now)
            .Where(g => !db.GrantRevocations.Any(r => r.GrantId == g.Id))
            .ToListAsync(ct);
        foreach (var grant in open)
        {
            var revocation = new GrantRevocation(Ids.New(), grant.Id, reason, actorId, now);
            db.GrantRevocations.Add(revocation);
            audit.Record(revocation, "offline_grant.revoked", reason, new Dictionary<string, object?> { ["grant_id"] = grant.Id.ToString("D") });
        }
    }

    /// <summary>The table of docs/lean-pilot/05 §5.5, applied to one command (the user and device were already checked ACTIVE).</summary>
    public async Task<GrantCheck> CheckAsync(Guid? grantId, Guid userId, Guid deviceId, TimeEstimate time, DateTimeOffset receivedAt, CancellationToken ct)
    {
        if (grantId is null)
        {
            return new GrantCheck(GrantOutcomes.Reject, TrustErrorCodes.GrantRequired, "Commands must carry the offline grant they were made under.", false);
        }

        var grant = await db.OfflineGrants.AsNoTracking().SingleOrDefaultAsync(g => g.Id == grantId, ct);
        if (grant is null || grant.UserId != userId || grant.DeviceId != deviceId)
        {
            return new GrantCheck(GrantOutcomes.Reject, TrustErrorCodes.GrantInvalid, "Unknown grant, or a grant of another device or user.", true);
        }

        var tolerance = TimeSpan.FromMinutes(await policies.IntAsync(PolicyKeys.GrantWindowToleranceMinutes, ct));
        var revocation = await db.GrantRevocations.AsNoTracking().Where(r => r.GrantId == grant.Id).OrderBy(r => r.RevokedAt).FirstOrDefaultAsync(ct);
        var windowEnd = grant.ExpiresAt + tolerance;
        var windowStart = grant.IssuedAt - tolerance;

        if (time.Confidence == TimeConfidence.High)
        {
            if (time.Estimated > windowEnd)
            {
                return new GrantCheck(GrantOutcomes.Reject, TrustErrorCodes.GrantExpired, "The command was made after the offline grant expired.", true);
            }

            if (revocation is not null && time.Estimated > revocation.RevokedAt + tolerance)
            {
                return new GrantCheck(GrantOutcomes.Reject, TrustErrorCodes.GrantRevoked, "The command was made after the grant was revoked.", false);
            }

            return time.Estimated < windowStart
                ? new GrantCheck(GrantOutcomes.Quarantine, TrustErrorCodes.GrantWindowUnproven, "The command time is before the grant was issued.", false)
                : new GrantCheck(GrantOutcomes.Ok, null, null, false);
        }

        // Low confidence (no clock sample for that boot): the command was made after the grant reached the phone and before the
        // server received it. It is inside the window only if the server received it before the window closed.
        var latestPossible = receivedAt;
        if (latestPossible <= windowEnd && (revocation is null || latestPossible <= revocation.RevokedAt + tolerance))
        {
            return new GrantCheck(GrantOutcomes.Ok, null, null, false);
        }

        return new GrantCheck(GrantOutcomes.Quarantine, TrustErrorCodes.GrantWindowUnproven,
            "The command time cannot be proven inside the grant window (the phone was restarted before it synced).", false);
    }

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
