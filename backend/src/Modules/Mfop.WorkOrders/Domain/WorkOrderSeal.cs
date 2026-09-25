using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Kernel.Json;

namespace Mfop.WorkOrders.Domain;

public sealed record SealEvidence(Guid Id, string Kind, string? Sha256Server, string State, Guid? SupersededBy);

public sealed record SealMaterial(Guid Id, string Type, Guid ItemId, decimal Quantity, IReadOnlyList<string> Serials, Guid FromLocationId, Guid ToLocationId, DateTimeOffset OccurredAt);

/// <summary>
/// The work-order seal (docs/lean-pilot/06 §6.1): SHA-256 over the canonical JSON of the work order, its events, arrivals,
/// evidence hashes, material movements, checklists, submissions and reviews. All values are strings, booleans, integers or
/// null, timestamps are UTC with microseconds. tools/audit-verify rebuilds exactly this document from the raw tables
/// (independent code) and compares; any later change to any of these rows is detected.
/// </summary>
public static class WorkOrderSeal
{
    public const string SealVersion = "1";

    public static JsonObject Document(
        WorkOrder w,
        IEnumerable<WorkOrderEvent> events,
        IEnumerable<WorkOrderArrival> arrivals,
        IEnumerable<SealEvidence> evidence,
        IEnumerable<SealMaterial> materials,
        IEnumerable<ChecklistResult> checklists,
        IEnumerable<WorkOrderSubmission> submissions,
        IEnumerable<WorkOrderReview> reviews)
    {
        var doc = new JsonObject
        {
            ["seal_version"] = SealVersion,
            ["work_order"] = new JsonObject
            {
                ["id"] = Id(w.Id),
                ["code"] = w.Code,
                ["fault_id"] = Id(w.FaultId),
                ["asset_id"] = Id(w.AssetId),
                ["priority"] = w.Priority,
                ["state"] = w.State,
                ["outcome"] = w.Outcome,
                ["diagnosis_code"] = w.DiagnosisCode,
                ["diagnosis_note"] = w.DiagnosisNote,
                ["root_cause_note"] = w.RootCauseNote,
                ["no_material_reason"] = w.NoMaterialReason,
                ["test_method"] = w.TestMethod,
                ["test_result"] = w.TestResult,
                ["checklist_template_id"] = Id(w.ChecklistTemplateId),
                ["rework_of_id"] = Id(w.ReworkOfId),
                ["assignee_id"] = Id(w.AssigneeId),
                ["closed_by"] = Id(w.ClosedBy),
                ["closed_at"] = Time(w.ClosedAt),
            },
            ["events"] = new JsonArray(events.OrderBy(e => e.Seq).Select(e => (JsonNode)new JsonObject
            {
                ["seq"] = e.Seq,
                ["type"] = e.Type,
                ["actor_id"] = Id(e.ActorId),
                ["device_id"] = Id(e.DeviceId),
                ["command_id"] = Id(e.CommandId),
                ["device_time"] = Time(e.DeviceTime),
                ["recorded_at"] = Time(e.RecordedAt),
                ["data"] = JsonNode.Parse(e.Data),
            }).ToArray()),
            ["arrivals"] = new JsonArray(arrivals.OrderBy(a => a.ArrivalNo).Select(a => (JsonNode)new JsonObject
            {
                ["id"] = Id(a.Id),
                ["arrival_no"] = a.ArrivalNo,
                ["user_id"] = Id(a.UserId),
                ["tag_scanned"] = a.TagScanned,
                ["scanned_code"] = a.ScannedCode,
                ["manual_code"] = a.ManualCode,
                ["manual_reason"] = a.ManualReason,
                ["recorded_at"] = Time(a.RecordedAt),
            }).ToArray()),
            ["evidence"] = new JsonArray(evidence.OrderBy(e => Id(e.Id), StringComparer.Ordinal).Select(e => (JsonNode)new JsonObject
            {
                ["id"] = Id(e.Id),
                ["kind"] = e.Kind,
                ["sha256_server"] = e.Sha256Server,
                ["state"] = e.State,
                ["superseded_by"] = Id(e.SupersededBy),
            }).ToArray()),
            ["materials"] = new JsonArray(materials.OrderBy(m => Id(m.Id), StringComparer.Ordinal).Select(m => (JsonNode)new JsonObject
            {
                ["id"] = Id(m.Id),
                ["type"] = m.Type,
                ["item_id"] = Id(m.ItemId),
                ["quantity"] = Quantity(m.Quantity),
                ["serials"] = new JsonArray(m.Serials.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
                ["from_location_id"] = Id(m.FromLocationId),
                ["to_location_id"] = Id(m.ToLocationId),
                ["occurred_at"] = Time(m.OccurredAt),
            }).ToArray()),
            ["checklists"] = new JsonArray(checklists.OrderBy(c => c.RecordedAt).ThenBy(c => Id(c.Id), StringComparer.Ordinal).Select(c => (JsonNode)new JsonObject
            {
                ["id"] = Id(c.Id),
                ["template_id"] = Id(c.TemplateId),
                ["test_method"] = c.TestMethod,
                ["not_tested_reason"] = c.NotTestedReason,
                ["test_result"] = c.TestResult,
                ["blocked"] = c.Blocked,
                ["answers"] = JsonNode.Parse(c.Answers),
                ["recorded_by"] = Id(c.RecordedBy),
                ["recorded_at"] = Time(c.RecordedAt),
            }).ToArray()),
            ["submissions"] = new JsonArray(submissions.OrderBy(s => s.SubmissionNo).Select(s => (JsonNode)new JsonObject
            {
                ["submission_no"] = s.SubmissionNo,
                ["submitted_by"] = Id(s.SubmittedBy),
                ["payload_hash"] = s.PayloadHash,
                ["submitted_at"] = Time(s.SubmittedAt),
            }).ToArray()),
            ["reviews"] = new JsonArray(reviews.OrderBy(r => r.DecidedAt).ThenBy(r => Id(r.Id), StringComparer.Ordinal).Select(r => (JsonNode)new JsonObject
            {
                ["id"] = Id(r.Id),
                ["submission_id"] = Id(r.SubmissionId),
                ["reviewer_id"] = Id(r.ReviewerId),
                ["decision"] = r.Decision,
                ["reasons"] = new JsonArray(r.Reasons.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
                ["note"] = r.Note,
                ["decided_at"] = Time(r.DecidedAt),
            }).ToArray()),
        };
        return doc;
    }

    public static string Hash(JsonObject document) => CanonicalJson.Sha256Hex(document);

    public static string Time(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    private static string? Time(DateTimeOffset? value) => value is { } v ? Time(v) : null;

    private static string Id(Guid value) => value.ToString("D");

    private static string? Id(Guid? value) => value?.ToString("D");

    public static string Quantity(decimal q) => q.ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>Canonical hash of a submission report (wo_submissions.payload_hash).</summary>
    public static string ReportHash(JsonObject report) => CanonicalJson.Sha256Hex(report);

    public static JsonElement Clone(JsonObject o) => JsonDocument.Parse(o.ToJsonString()).RootElement.Clone();
}
