using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace Mfop.AuditVerify;

public sealed record Finding(string Code, long? Seq, string? EntityType, string? EntityId, string Message);

public sealed class VerificationReport
{
    public List<Finding> Findings { get; } = [];
    public long EventsChecked { get; set; }
    public long VersionsChecked { get; set; }
    public long AssetsChecked { get; set; }
    public long WorkOrdersChecked { get; set; }
    public long MovementsChecked { get; set; }
    public bool Ok => Findings.Count == 0;
}

/// <summary>
/// Recomputes, from the raw database rows, everything the application claims:
///  1. audit chain: seq is 1..n without gaps; each payload_hash matches the row; each prev_hash links to the
///     previous hash; each hash = SHA-256(prev || payload || seq).
///  2. approved asset versions: snapshot_hash matches the stored snapshot and the value recorded in the audit log.
///  3. active street lights: the current row still equals its latest approved snapshot.
///  4. closed work orders: the seal recomputed from the operational rows equals the stored and the audited seal (M3).
///  5. stock balances equal the sum of the append-only movements (M3).
/// Limitation (documented, ADR-0007): deleting the most recent events leaves a shorter but valid chain; that is
/// detected by signed checkpoints stored outside the database (M8), and partially by check 2 when approvals disappear.
/// </summary>
public static class Verifier
{
    private static readonly string Genesis = new('0', 64);

    public static async Task<VerificationReport> RunAsync(string connectionString, CancellationToken ct = default)
    {
        var report = new VerificationReport();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct); // one consistent snapshot

        var auditedSeals = new Dictionary<string, string?>();
        var versionAuditHashes = await CheckChainAsync(conn, tx, report, auditedSeals, ct);
        var latestSnapshots = await CheckVersionsAsync(conn, tx, report, versionAuditHashes, ct);
        await CheckActiveAssetsAsync(conn, tx, report, latestSnapshots, ct);
        await WorkOrderSeals.CheckAsync(conn, tx, report, auditedSeals, ct);
        await WorkOrderSeals.CheckBalancesAsync(conn, tx, report, ct);
        return report;
    }

    private static async Task<Dictionary<string, string?>> CheckChainAsync(NpgsqlConnection conn, NpgsqlTransaction tx, VerificationReport report,
        Dictionary<string, string?> auditedSeals, CancellationToken ct)
    {
        var versionHashes = new Dictionary<string, string?>();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT seq, event_id, occurred_at, actor_user_id, actor_roles, device_id, action, entity_type, entity_id,
                   entity_version, changes::text, facts::text, reason, command_id, correlation_id, payload_hash, prev_hash, hash
            FROM audit.audit_events ORDER BY seq
            """, conn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        long expected = 1;
        var previousHash = Genesis;
        while (await r.ReadAsync(ct))
        {
            report.EventsChecked++;
            var seq = r.GetInt64(0);
            var entityType = r.GetString(7);
            var entityId = r.GetString(8);
            var action = r.GetString(6);
            var changes = r.GetString(10);

            if (seq != expected)
            {
                report.Findings.Add(new("SEQ_GAP", seq, entityType, entityId, $"expected seq {expected}, found {seq} (events missing or reordered)"));
                expected = seq;
            }

            var payload = PayloadJson(r);
            var payloadHash = Canonical.Sha256Hex(payload);
            var storedPayloadHash = r.GetString(15).Trim();
            var storedPrev = r.GetString(16).Trim();
            var storedHash = r.GetString(17).Trim();

            if (payloadHash != storedPayloadHash)
            {
                report.Findings.Add(new("EVENT_CONTENT_CHANGED", seq, entityType, entityId, $"event content does not match its payload_hash (action {action})"));
            }

            if (storedPrev != previousHash)
            {
                report.Findings.Add(new("CHAIN_LINK_BROKEN", seq, entityType, entityId, "prev_hash does not equal the previous event's hash"));
            }

            if (ChainHash(storedPrev, storedPayloadHash, seq) != storedHash)
            {
                report.Findings.Add(new("EVENT_HASH_INVALID", seq, entityType, entityId, "hash is not SHA-256(prev_hash || payload_hash || seq)"));
            }

            if (entityType == "asset_version" && action == "asset_version.created")
            {
                using var doc = JsonDocument.Parse(changes);
                versionHashes[entityId] = doc.RootElement.TryGetProperty("snapshot_hash", out var sh) && sh.TryGetProperty("new", out var v) ? v.GetString() : null;
            }

            if (entityType == "work_order" && action == "work_order.closed" && !r.IsDBNull(11))
            {
                using var facts = JsonDocument.Parse(r.GetString(11));
                auditedSeals[entityId] = facts.RootElement.TryGetProperty("seal_hash", out var seal) ? seal.GetString() : null;
            }

            previousHash = storedHash;
            expected++;
        }

        return versionHashes;
    }

    private static JsonElement PayloadJson(NpgsqlDataReader r)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("event_id", r.GetGuid(1).ToString("D"));
            w.WriteString("occurred_at", r.GetFieldValue<DateTime>(2).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
            WriteNullableString(w, "actor_user_id", r.IsDBNull(3) ? null : r.GetGuid(3).ToString("D"));
            w.WriteStartArray("actor_roles");
            foreach (var role in r.GetFieldValue<string[]>(4)) w.WriteStringValue(role);
            w.WriteEndArray();
            WriteNullableString(w, "device_id", r.IsDBNull(5) ? null : r.GetGuid(5).ToString("D"));
            w.WriteString("action", r.GetString(6));
            w.WriteString("entity_type", r.GetString(7));
            w.WriteString("entity_id", r.GetString(8));
            if (r.IsDBNull(9)) w.WriteNull("entity_version"); else w.WriteNumber("entity_version", r.GetInt32(9));
            w.WritePropertyName("changes");
            JsonDocument.Parse(r.GetString(10)).RootElement.WriteTo(w);
            w.WritePropertyName("facts");
            if (r.IsDBNull(11)) w.WriteNullValue(); else JsonDocument.Parse(r.GetString(11)).RootElement.WriteTo(w);
            WriteNullableString(w, "reason", r.IsDBNull(12) ? null : r.GetString(12));
            w.WriteString("command_id", r.GetGuid(13).ToString("D"));
            WriteNullableString(w, "correlation_id", r.IsDBNull(14) ? null : r.GetString(14));
            w.WriteEndObject();
        }

        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private static async Task<Dictionary<Guid, JsonElement>> CheckVersionsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, VerificationReport report, Dictionary<string, string?> auditHashes, CancellationToken ct)
    {
        var latest = new Dictionary<Guid, (int No, JsonElement Snapshot)>();
        await using var cmd = new NpgsqlCommand("SELECT id, asset_id, version_no, snapshot::text, snapshot_hash FROM assets.asset_versions ORDER BY asset_id, version_no", conn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            report.VersionsChecked++;
            var id = r.GetGuid(0).ToString("D");
            var assetId = r.GetGuid(1);
            var snapshot = JsonDocument.Parse(r.GetString(3)).RootElement.Clone();
            var storedHash = r.GetString(4).Trim();

            if (Canonical.Sha256Hex(snapshot) != storedHash)
            {
                report.Findings.Add(new("SNAPSHOT_HASH_MISMATCH", null, "asset_version", id, "stored snapshot does not match snapshot_hash"));
            }

            if (!auditHashes.TryGetValue(id, out var audited))
            {
                report.Findings.Add(new("VERSION_NOT_IN_AUDIT", null, "asset_version", id, "no asset_version.created audit event for this version"));
            }
            else if (audited != storedHash)
            {
                report.Findings.Add(new("VERSION_HASH_DIFFERS_FROM_AUDIT", null, "asset_version", id, "snapshot_hash differs from the value recorded in the audit log"));
            }

            latest[assetId] = (r.GetInt32(2), snapshot);
        }

        return latest.ToDictionary(k => k.Key, v => v.Value.Snapshot);
    }

    private static async Task CheckActiveAssetsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, VerificationReport report, Dictionary<Guid, JsonElement> latest, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT s.id, s.asset_code, s.lifecycle_state, s.latitude, s.longitude, s.address_text, s.circuit_ref, s.power_source,
                   s.pole_material, s.pole_height_m, s.luminaire_make, s.luminaire_model, s.luminaire_wattage_w, s.luminaire_serial,
                   s.solar_panel_w, s.solar_battery_type, s.solar_battery_ah, s.solar_controller_model, s.installed_on,
                   s.installed_on_precision, s.warranty_until, s.supplier, s.responsible_party, s.responsible_entity_name, t.code
            FROM assets.street_lights s LEFT JOIN assets.tags t ON t.id = s.current_tag_id
            WHERE s.lifecycle_state = 'ACTIVE'
            """, conn, tx);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            report.AssetsChecked++;
            var id = r.GetGuid(0);
            var code = r.IsDBNull(1) ? null : r.GetString(1);
            var current = new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["asset_code"] = code,
                ["lifecycle_state"] = r.GetString(2),
                ["latitude"] = Dec(r.GetDecimal(3)),
                ["longitude"] = Dec(r.GetDecimal(4)),
                ["address_text"] = S(r, 5),
                ["circuit_ref"] = S(r, 6),
                ["power_source"] = r.GetString(7),
                ["pole_material"] = S(r, 8),
                ["pole_height_m"] = r.IsDBNull(9) ? null : Dec(r.GetDecimal(9)),
                ["luminaire_make"] = S(r, 10),
                ["luminaire_model"] = S(r, 11),
                ["luminaire_wattage_w"] = I(r, 12),
                ["luminaire_serial"] = S(r, 13),
                ["solar_panel_w"] = I(r, 14),
                ["solar_battery_type"] = S(r, 15),
                ["solar_battery_ah"] = I(r, 16),
                ["solar_controller_model"] = S(r, 17),
                ["installed_on"] = D(r, 18),
                ["installed_on_precision"] = r.GetString(19),
                ["warranty_until"] = D(r, 20),
                ["supplier"] = S(r, 21),
                ["responsible_party"] = r.GetString(22),
                ["responsible_entity_name"] = S(r, 23),
                ["tag_code"] = r.IsDBNull(24) ? null : r.GetString(24).Trim(),
            };

            if (!latest.TryGetValue(id, out var snapshot))
            {
                report.Findings.Add(new("ACTIVE_ASSET_WITHOUT_VERSION", null, "street_light", id.ToString("D"), $"{code} is ACTIVE but has no approved version"));
                continue;
            }

            var differing = current.Where(kv => !snapshot.TryGetProperty(kv.Key, out var v) || (v.ValueKind == JsonValueKind.Null ? null : v.GetString()) != kv.Value)
                .Select(kv => kv.Key).ToList();
            if (differing.Count > 0)
            {
                report.Findings.Add(new("ASSET_ROW_CHANGED", null, "street_light", id.ToString("D"),
                    $"{code}: current row differs from its latest approved version in: {string.Join(", ", differing)}"));
            }
        }
    }

    private static string ChainHash(string prevHex, string payloadHex, long seq)
    {
        try
        {
            var buffer = new byte[72];
            Convert.FromHexString(prevHex).CopyTo(buffer, 0);
            Convert.FromHexString(payloadHex).CopyTo(buffer, 32);
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(64), seq);
            return Convert.ToHexStringLower(SHA256.HashData(buffer));
        }
        catch (FormatException)
        {
            return "invalid-hex";
        }
    }

    private static void WriteNullableString(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null) w.WriteNull(name); else w.WriteString(name, value);
    }

    private static string Dec(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string? S(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static string? I(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i).ToString(CultureInfo.InvariantCulture);

    private static string? D(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetFieldValue<DateOnly>(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
