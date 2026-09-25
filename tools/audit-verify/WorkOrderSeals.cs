using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace Mfop.AuditVerify;

/// <summary>
/// Recomputes, independently of the application code, the seal of every CLOSED work order from the operational tables
/// (work order, events, arrivals, evidence hashes, material movements, checklists, submissions, reviews) and compares it
/// with the stored seal and with the seal recorded in the audit log (docs/lean-pilot/06 §6.1). Then recomputes every
/// stock balance from the append-only movements.
/// </summary>
public static class WorkOrderSeals
{
    public static async Task CheckAsync(NpgsqlConnection conn, NpgsqlTransaction tx, VerificationReport report, IReadOnlyDictionary<string, string?> auditedSeals, CancellationToken ct)
    {
        var workOrders = new List<Dictionary<string, object?>>();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT id, code, fault_id, asset_id, priority, state, outcome, diagnosis_code, diagnosis_note, root_cause_note, no_material_reason,
                   test_method, test_result, checklist_template_id, rework_of_id, assignee_id, closed_by, closed_at, seal_hash
            FROM workorders.work_orders WHERE state = 'CLOSED' ORDER BY code
            """, conn, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                workOrders.Add(row);
            }
        }

        foreach (var w in workOrders)
        {
            report.WorkOrdersChecked++;
            var id = (Guid)w["id"]!;
            var code = (string)w["code"]!;
            var recomputed = Canonical.Sha256Hex(await DocumentAsync(conn, tx, w, id, ct));
            var stored = (w["seal_hash"] as string)?.Trim();
            if (stored is null)
            {
                report.Findings.Add(new("SEAL_MISSING", null, "work_order", id.ToString("D"), $"{code} is CLOSED but has no seal"));
                continue;
            }

            if (recomputed != stored)
            {
                report.Findings.Add(new("SEAL_MISMATCH", null, "work_order", id.ToString("D"),
                    $"{code}: the work order's records no longer match its seal (a row of the work order, its events, evidence, materials, checklist, submissions or reviews was changed)"));
            }

            if (!auditedSeals.TryGetValue(id.ToString("D"), out var audited) || audited != stored)
            {
                report.Findings.Add(new("SEAL_DIFFERS_FROM_AUDIT", null, "work_order", id.ToString("D"), $"{code}: stored seal differs from the seal recorded in the audit log"));
            }
        }
    }

    public static async Task CheckBalancesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, VerificationReport report, CancellationToken ct)
    {
        var computed = new Dictionary<(Guid Location, Guid Item), decimal>();
        await using (var cmd = new NpgsqlCommand("SELECT from_location_id, to_location_id, item_id, quantity FROM inventory.stock_movements", conn, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                report.MovementsChecked++;
                var q = r.GetDecimal(3);
                var item = r.GetGuid(2);
                computed[(r.GetGuid(0), item)] = computed.GetValueOrDefault((r.GetGuid(0), item)) - q;
                computed[(r.GetGuid(1), item)] = computed.GetValueOrDefault((r.GetGuid(1), item)) + q;
            }
        }

        var stored = new Dictionary<(Guid Location, Guid Item), decimal>();
        var labels = new Dictionary<(Guid, Guid), string>();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT b.location_id, b.item_id, b.quantity, l.kind, i.sku
            FROM inventory.stock_balances b JOIN inventory.stock_locations l ON l.id = b.location_id JOIN inventory.items i ON i.id = b.item_id
            """, conn, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                stored[(r.GetGuid(0), r.GetGuid(1))] = r.GetDecimal(2);
                labels[(r.GetGuid(0), r.GetGuid(1))] = $"{r.GetString(4)} at {r.GetString(3)} {r.GetGuid(0):D}";
            }
        }

        foreach (var key in computed.Keys.Union(stored.Keys))
        {
            var c = computed.GetValueOrDefault(key);
            var s = stored.GetValueOrDefault(key);
            if (c != s)
            {
                report.Findings.Add(new("STOCK_BALANCE_MISMATCH", null, "stock_balance", $"{key.Location:D}/{key.Item:D}",
                    $"{labels.GetValueOrDefault(key, "item " + key.Item.ToString("D"))}: stored {Q(s)} but the movements add up to {Q(c)}"));
            }
        }
    }

    /// <summary>
    /// M5: what is IN_TRANSIT must be exactly the lines of the issues not yet acknowledged/cancelled and of the returns not yet
    /// decided, and what is in CORE_BIN must be exactly the received core returns (independent SQL over the raw rows).
    /// </summary>
    public static async Task CheckInventoryDocumentsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, VerificationReport report, CancellationToken ct)
    {
        async Task<Dictionary<Guid, decimal>> SumAsync(string sql)
        {
            var sums = new Dictionary<Guid, decimal>();
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                sums[r.GetGuid(0)] = r.GetDecimal(1);
            }

            return sums;
        }

        var ledgerInTransit = await SumAsync(
            """
            SELECT m.item_id, sum(CASE WHEN m.to_location_id = l.id THEN m.quantity ELSE -m.quantity END)
            FROM inventory.stock_movements m JOIN inventory.stock_locations l ON l.kind = 'IN_TRANSIT' AND (m.to_location_id = l.id OR m.from_location_id = l.id)
            GROUP BY m.item_id
            """);
        var openDocuments = await SumAsync(
            """
            SELECT m.item_id, sum(m.quantity) FROM inventory.stock_movements m
            WHERE (m.type = 'ISSUE' AND m.document_id IN (SELECT id FROM inventory.stock_issues WHERE state IN ('ISSUED', 'DISPUTED')))
               OR (m.type = 'RETURN_DECLARE' AND m.document_id IN (SELECT id FROM inventory.stock_returns WHERE state = 'DECLARED'))
            GROUP BY m.item_id
            """);
        foreach (var item in ledgerInTransit.Keys.Union(openDocuments.Keys))
        {
            var ledger = ledgerInTransit.GetValueOrDefault(item);
            var open = openDocuments.GetValueOrDefault(item);
            if (ledger != open)
            {
                report.Findings.Add(new("IN_TRANSIT_MISMATCH", null, "item", item.ToString("D"), $"in transit {Q(ledger)} but open issues/returns hold {Q(open)}"));
            }
        }

        var ledgerCoreBin = await SumAsync(
            """
            SELECT m.item_id, sum(CASE WHEN m.to_location_id = l.id THEN m.quantity ELSE -m.quantity END)
            FROM inventory.stock_movements m JOIN inventory.stock_locations l ON l.kind = 'CORE_BIN' AND (m.to_location_id = l.id OR m.from_location_id = l.id)
            GROUP BY m.item_id
            """);
        var received = await SumAsync("SELECT item_id, sum(quantity) FROM inventory.core_returns WHERE state = 'RECEIVED' GROUP BY item_id");
        foreach (var item in ledgerCoreBin.Keys.Union(received.Keys))
        {
            var ledger = ledgerCoreBin.GetValueOrDefault(item);
            var cores = received.GetValueOrDefault(item);
            if (ledger != cores)
            {
                report.Findings.Add(new("CORE_BIN_MISMATCH", null, "item", item.ToString("D"), $"core bin {Q(ledger)} but received core returns add up to {Q(cores)}"));
            }
        }
    }

    private static async Task<JsonElement> DocumentAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Dictionary<string, object?> w, Guid id, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            j.WriteString("seal_version", "1");
            j.WritePropertyName("work_order");
            j.WriteStartObject();
            Str(j, "id", G(w["id"]));
            Str(j, "code", (string?)w["code"]);
            Str(j, "fault_id", G(w["fault_id"]));
            Str(j, "asset_id", G(w["asset_id"]));
            foreach (var column in new[] { "priority", "state", "outcome", "diagnosis_code", "diagnosis_note", "root_cause_note", "no_material_reason", "test_method", "test_result" })
            {
                Str(j, column, (string?)w[column]);
            }

            Str(j, "checklist_template_id", G(w["checklist_template_id"]));
            Str(j, "rework_of_id", G(w["rework_of_id"]));
            Str(j, "assignee_id", G(w["assignee_id"]));
            Str(j, "closed_by", G(w["closed_by"]));
            Str(j, "closed_at", T(w["closed_at"]));
            j.WriteEndObject();

            await ArrayAsync(j, conn, tx, "events",
                "SELECT seq, type, actor_id, device_id, command_id, device_time, recorded_at, data::text FROM workorders.wo_events WHERE work_order_id = @id ORDER BY seq", id,
                r =>
                {
                    j.WriteNumber("seq", r.GetInt32(0));
                    Str(j, "type", r.GetString(1));
                    Str(j, "actor_id", Gr(r, 2));
                    Str(j, "device_id", Gr(r, 3));
                    Str(j, "command_id", r.GetGuid(4).ToString("D"));
                    Str(j, "device_time", Tr(r, 5));
                    Str(j, "recorded_at", Tr(r, 6));
                    j.WritePropertyName("data");
                    JsonDocument.Parse(r.GetString(7)).RootElement.WriteTo(j);
                }, ct);

            await ArrayAsync(j, conn, tx, "arrivals",
                "SELECT id, arrival_no, user_id, tag_scanned, scanned_code, manual_code, manual_reason, recorded_at FROM workorders.wo_arrivals WHERE work_order_id = @id ORDER BY arrival_no", id,
                r =>
                {
                    Str(j, "id", r.GetGuid(0).ToString("D"));
                    j.WriteNumber("arrival_no", r.GetInt32(1));
                    Str(j, "user_id", r.GetGuid(2).ToString("D"));
                    j.WriteBoolean("tag_scanned", r.GetBoolean(3));
                    Str(j, "scanned_code", Sr(r, 4));
                    Str(j, "manual_code", Sr(r, 5));
                    Str(j, "manual_reason", Sr(r, 6));
                    Str(j, "recorded_at", Tr(r, 7));
                }, ct);

            await ArrayAsync(j, conn, tx, "evidence",
                "SELECT id, kind, sha256_server, state, superseded_by FROM evidence.evidence WHERE owner_type = 'WORK_ORDER' AND owner_id = @id ORDER BY id::text COLLATE \"C\"", id,
                r =>
                {
                    Str(j, "id", r.GetGuid(0).ToString("D"));
                    Str(j, "kind", r.GetString(1));
                    Str(j, "sha256_server", Sr(r, 2)?.Trim());
                    Str(j, "state", r.GetString(3));
                    Str(j, "superseded_by", Gr(r, 4));
                }, ct);

            await ArrayAsync(j, conn, tx, "materials",
                "SELECT id, type, item_id, quantity, serials, from_location_id, to_location_id, occurred_at FROM inventory.stock_movements WHERE work_order_id = @id ORDER BY id::text COLLATE \"C\"", id,
                r =>
                {
                    Str(j, "id", r.GetGuid(0).ToString("D"));
                    Str(j, "type", r.GetString(1));
                    Str(j, "item_id", r.GetGuid(2).ToString("D"));
                    Str(j, "quantity", Q(r.GetDecimal(3)));
                    j.WriteStartArray("serials");
                    foreach (var s in r.GetFieldValue<string[]>(4)) j.WriteStringValue(s);
                    j.WriteEndArray();
                    Str(j, "from_location_id", r.GetGuid(5).ToString("D"));
                    Str(j, "to_location_id", r.GetGuid(6).ToString("D"));
                    Str(j, "occurred_at", Tr(r, 7));
                }, ct);

            await ArrayAsync(j, conn, tx, "checklists",
                "SELECT id, template_id, test_method, not_tested_reason, test_result, blocked, answers::text, recorded_by, recorded_at FROM workorders.checklist_results WHERE work_order_id = @id ORDER BY recorded_at, id::text COLLATE \"C\"", id,
                r =>
                {
                    Str(j, "id", r.GetGuid(0).ToString("D"));
                    Str(j, "template_id", r.GetGuid(1).ToString("D"));
                    Str(j, "test_method", r.GetString(2));
                    Str(j, "not_tested_reason", Sr(r, 3));
                    Str(j, "test_result", r.GetString(4));
                    j.WriteBoolean("blocked", r.GetBoolean(5));
                    j.WritePropertyName("answers");
                    JsonDocument.Parse(r.GetString(6)).RootElement.WriteTo(j);
                    Str(j, "recorded_by", r.GetGuid(7).ToString("D"));
                    Str(j, "recorded_at", Tr(r, 8));
                }, ct);

            await ArrayAsync(j, conn, tx, "submissions",
                "SELECT submission_no, submitted_by, payload_hash, submitted_at FROM workorders.wo_submissions WHERE work_order_id = @id ORDER BY submission_no", id,
                r =>
                {
                    j.WriteNumber("submission_no", r.GetInt32(0));
                    Str(j, "submitted_by", r.GetGuid(1).ToString("D"));
                    Str(j, "payload_hash", r.GetString(2).Trim());
                    Str(j, "submitted_at", Tr(r, 3));
                }, ct);

            await ArrayAsync(j, conn, tx, "reviews",
                "SELECT id, submission_id, reviewer_id, decision, reasons, note, decided_at FROM workorders.wo_reviews WHERE work_order_id = @id ORDER BY decided_at, id::text COLLATE \"C\"", id,
                r =>
                {
                    Str(j, "id", r.GetGuid(0).ToString("D"));
                    Str(j, "submission_id", r.GetGuid(1).ToString("D"));
                    Str(j, "reviewer_id", r.GetGuid(2).ToString("D"));
                    Str(j, "decision", r.GetString(3));
                    j.WriteStartArray("reasons");
                    foreach (var s in r.GetFieldValue<string[]>(4)) j.WriteStringValue(s);
                    j.WriteEndArray();
                    Str(j, "note", Sr(r, 5));
                    Str(j, "decided_at", Tr(r, 6));
                }, ct);
            j.WriteEndObject();
        }

        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private static async Task ArrayAsync(Utf8JsonWriter j, NpgsqlConnection conn, NpgsqlTransaction tx, string name, string sql, Guid id, Action<NpgsqlDataReader> item, CancellationToken ct)
    {
        j.WriteStartArray(name);
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            j.WriteStartObject();
            item(r);
            j.WriteEndObject();
        }

        j.WriteEndArray();
    }

    private static void Str(Utf8JsonWriter j, string name, string? value)
    {
        if (value is null) j.WriteNull(name); else j.WriteString(name, value);
    }

    private static string? G(object? v) => v is Guid g ? g.ToString("D") : null;

    private static string? T(object? v) => v is DateTime d ? Fmt(d) : v is DateTimeOffset o ? Fmt(o.UtcDateTime) : null;

    private static string? Gr(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i).ToString("D");

    private static string? Sr(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static string? Tr(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : Fmt(r.GetFieldValue<DateTime>(i));

    private static string Fmt(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static string Q(decimal q) => q.ToString("0.000", CultureInfo.InvariantCulture);
}
