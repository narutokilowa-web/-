using System.Text.Json.Nodes;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;

namespace Mfop.WorkOrders.Application;

/// <summary>Appends wo_events (append-only, part of the seal). Data values must be strings, booleans or null.</summary>
public static class WorkOrderEventLog
{
    public const string Created = "CREATED", Assigned = "ASSIGNED", Reassigned = "REASSIGNED", Cancelled = "CANCELLED", Arrived = "ARRIVED",
        EvidenceAdded = "EVIDENCE_ADDED", DiagnosisRecorded = "DIAGNOSIS_RECORDED", WorkStarted = "WORK_STARTED", MaterialConsumed = "MATERIAL_CONSUMED",
        ChecklistRecorded = "CHECKLIST_RECORDED", Submitted = "SUBMITTED", Resubmitted = "RESUBMITTED", Returned = "RETURNED", Approved = "APPROVED",
        CoreReturnDeclared = "CORE_RETURN_DECLARED";

    public static WorkOrderEvent Append(WorkOrdersDbContext db, IAuditTrail audit, WorkOrder workOrder, string type, CommandEnvelope cmd, DateTimeOffset now,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        var json = new JsonObject();
        foreach (var (key, value) in (data ?? new Dictionary<string, string?>()).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            json[key] = value;
        }

        var e = new WorkOrderEvent(Ids.New(), workOrder.Id, workOrder.NextEventSeq(), type, cmd.Actor.UserId, cmd.Actor.DeviceId, cmd.CommandId,
            json.ToJsonString(), cmd.DeviceTime is { } t ? ClockExtensions.TruncateToMicros(t) : null, now);
        db.Events.Add(e);
        audit.Record(e, "work_order_event.appended", extra: new Dictionary<string, object?> { ["type"] = type, ["seq"] = e.Seq });
        return e;
    }
}
