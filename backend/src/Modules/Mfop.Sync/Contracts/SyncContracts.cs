using System.Text.Json;
using Mfop.Assets.Contracts;
using Mfop.Inventory.Contracts;
using Mfop.Rules.Contracts;
using Mfop.WorkOrders.Contracts;

namespace Mfop.Sync.Contracts;

/// <param name="CommandId">UUID generated on the phone (UUIDv7); the idempotency key.</param>
/// <param name="DeviceTime">Phone wall clock at capture (recorded; never trusted on its own).</param>
/// <param name="GrantId">M4: the offline grant the command was made under (docs/lean-pilot/05 §5.5). Required.</param>
/// <param name="ElapsedMs">M4: phone monotonic clock (ms since boot) at capture; with <paramref name="BootId"/> it gives trusted time (05 §5.7).</param>
/// <param name="BaseVersion">
/// M4: the version of the target the phone showed when the user acted (work orders: the last event seq pulled). A command that
/// would overwrite data changed since then by someone else is refused (WORK_ORDER_CHANGED), never applied silently.
/// </param>
public sealed record PushCommand(Guid CommandId, string Type, JsonElement Payload, DateTimeOffset? DeviceTime,
    Guid? GrantId = null, long? ElapsedMs = null, string? BootId = null, long? BaseVersion = null);

/// <param name="PendingCommands">How many commands the phone still holds after this batch (shown to supervisors).</param>
public sealed record PushRequest(IReadOnlyList<PushCommand> Commands, int? PendingCommands);

/// <param name="Result">
/// APPLIED, DUPLICATE (already applied earlier: treat as success), REJECTED, or QUARANTINED (kept for a supervisor's decision
/// because its time cannot be proven inside the offline grant; docs/lean-pilot/05 §5.5).
/// </param>
public sealed record PushCommandResult(Guid CommandId, string Result, string? Code, string? Message, Guid? EntityId);

public sealed record PushResponse(DateTimeOffset ServerTime, IReadOnlyList<PushCommandResult> Results);

public sealed record PullUser(Guid Id, string DisplayName, IReadOnlyList<string> Roles);

/// <param name="Cursor">Opaque. Pull always returns the full current task set; incremental pull arrives with M4.</param>
/// <param name="Reference">Diagnosis codes and checklist templates (M3), so the wizard works offline.</param>
/// <param name="Custody">The technician's custody (items, quantities, serials); empty for other roles.</param>
/// <param name="Policy">M4: offline grace, app lock and minimum app version, read from the policy settings.</param>
public sealed record PullResponse(DateTimeOffset ServerTime, string Cursor, PullUser User, Guid DeviceId, IReadOnlyList<WorkOrderTask> WorkOrders,
    FieldReferenceData? Reference = null, IReadOnlyList<CustodyItem>? Custody = null, IReadOnlyList<ItemDto>? Items = null,
    Mfop.Access.Contracts.ClientPolicyDto? Policy = null);

/// <summary>A command held for a supervisor's decision (docs/lean-pilot/05 §5.5, 03: "resolve sync quarantine").</summary>
public sealed record QuarantineDto(
    Guid Id, Guid CommandId, string Type, Guid UserId, string UserName, Guid DeviceId, string ReasonCode, string Reason,
    DateTimeOffset? DeviceTime, DateTimeOffset EstimatedTime, string TimeConfidence, DateTimeOffset ReceivedAt, Guid? GrantId,
    DateTimeOffset? GrantExpiresAt, JsonElement Payload, string State, string? ResolvedByName, DateTimeOffset? ResolvedAt,
    string? ResolutionReason, string? ResultCode);

/// <param name="Decision">APPLY (run the command now, as the technician) or DISCARD (keep it on record only).</param>
public sealed record ResolveQuarantineRequest(string? Decision, string? Reason);

/// <summary>What the phone shows after scanning a tag (docs/lean-pilot/08 §8.3).</summary>
public sealed record AssetCard(
    AssetSummary Asset,
    int OpenFaults,
    IReadOnlyList<WorkOrderTask> MyOpenTasks,
    IReadOnlyList<AssetCardEvent> RecentEvents,
    IReadOnlyList<RecommendationDto>? Recommendations = null);

public sealed record AssetCardEvent(DateTimeOffset At, string Action, string ActorName);
