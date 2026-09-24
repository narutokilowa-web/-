using Mfop.Infrastructure.Auditing;
using Mfop.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Mfop.Audit.Infrastructure;

/// <summary>
/// Appends events inside the caller's transaction. A transaction-scoped advisory lock serialises appends so that
/// seq is gap-free and each prev_hash is the true predecessor (cheap at pilot volume; see docs/lean-pilot/06).
/// </summary>
public sealed class AuditAppender : IAuditAppender
{
    /// <summary>Arbitrary constant identifying the audit-chain lock.</summary>
    public const long ChainLockKey = 7_201_001;

    public async Task AppendAsync(DbSession session, IReadOnlyList<AuditEventDraft> events, CancellationToken ct)
    {
        if (events.Count == 0)
        {
            return;
        }

        var tx = session.Transaction ?? throw new InvalidOperationException("Audit events must be appended inside the unit-of-work transaction.");
        var conn = session.Connection;

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@k)", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("k", ChainLockKey);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        long seq = 0;
        var prevHash = AuditHashing.GenesisHash;
        await using (var lastCmd = new NpgsqlCommand("SELECT seq, hash FROM audit.audit_events ORDER BY seq DESC LIMIT 1", conn, tx))
        await using (var reader = await lastCmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                seq = reader.GetInt64(0);
                prevHash = reader.GetString(1);
            }
        }

        foreach (var e in events)
        {
            seq++;
            var payloadHash = AuditHashing.PayloadHash(e);
            var hash = AuditHashing.ChainHash(prevHash, payloadHash, seq);

            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO audit.audit_events
                  (seq, event_id, occurred_at, actor_user_id, actor_roles, device_id, action, entity_type, entity_id,
                   entity_version, changes, facts, reason, command_id, correlation_id, payload_hash, prev_hash, hash)
                VALUES
                  (@seq, @event_id, @occurred_at, @actor_user_id, @actor_roles, @device_id, @action, @entity_type, @entity_id,
                   @entity_version, @changes, @facts, @reason, @command_id, @correlation_id, @payload_hash, @prev_hash, @hash)
                """, conn, tx);
            insert.Parameters.AddWithValue("seq", seq);
            insert.Parameters.AddWithValue("event_id", e.EventId);
            insert.Parameters.AddWithValue("occurred_at", e.OccurredAt);
            insert.Parameters.AddWithValue("actor_user_id", (object?)e.ActorUserId ?? DBNull.Value);
            insert.Parameters.AddWithValue("actor_roles", e.ActorRoles.ToArray());
            insert.Parameters.AddWithValue("device_id", (object?)e.DeviceId ?? DBNull.Value);
            insert.Parameters.AddWithValue("action", e.Action);
            insert.Parameters.AddWithValue("entity_type", e.EntityType);
            insert.Parameters.AddWithValue("entity_id", e.EntityId);
            insert.Parameters.AddWithValue("entity_version", (object?)e.EntityVersion ?? DBNull.Value);
            insert.Parameters.Add(new NpgsqlParameter("changes", NpgsqlDbType.Jsonb) { Value = e.Changes.ToJsonString() });
            insert.Parameters.Add(new NpgsqlParameter("facts", NpgsqlDbType.Jsonb) { Value = (object?)e.Facts?.ToJsonString() ?? DBNull.Value });
            insert.Parameters.AddWithValue("reason", (object?)e.Reason ?? DBNull.Value);
            insert.Parameters.AddWithValue("command_id", e.CommandId);
            insert.Parameters.AddWithValue("correlation_id", (object?)e.CorrelationId ?? DBNull.Value);
            insert.Parameters.AddWithValue("payload_hash", payloadHash);
            insert.Parameters.AddWithValue("prev_hash", prevHash);
            insert.Parameters.AddWithValue("hash", hash);
            await insert.ExecuteNonQueryAsync(ct);

            prevHash = hash;
        }
    }
}
