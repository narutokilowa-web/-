using System.Text.Json;
using Mfop.Assets.Contracts;
using Mfop.WorkOrders.Contracts;

namespace Mfop.Sync.Contracts;

/// <param name="CommandId">UUID generated on the phone (UUIDv7); the idempotency key.</param>
/// <param name="DeviceTime">Phone clock at capture (recorded; trusted-time estimation arrives in M4).</param>
public sealed record PushCommand(Guid CommandId, string Type, JsonElement Payload, DateTimeOffset? DeviceTime);

/// <param name="PendingCommands">How many commands the phone still holds after this batch (shown to supervisors).</param>
public sealed record PushRequest(IReadOnlyList<PushCommand> Commands, int? PendingCommands);

/// <param name="Result">APPLIED, DUPLICATE (already applied earlier: treat as success) or REJECTED.</param>
public sealed record PushCommandResult(Guid CommandId, string Result, string? Code, string? Message, Guid? EntityId);

public sealed record PushResponse(DateTimeOffset ServerTime, IReadOnlyList<PushCommandResult> Results);

public sealed record PullUser(Guid Id, string DisplayName, IReadOnlyList<string> Roles);

/// <param name="Cursor">Opaque. M2 pull always returns the full current task set; incremental pull arrives with M4.</param>
public sealed record PullResponse(DateTimeOffset ServerTime, string Cursor, PullUser User, Guid DeviceId, IReadOnlyList<WorkOrderTask> WorkOrders);

/// <summary>What the phone shows after scanning a tag (docs/lean-pilot/08 §8.3).</summary>
public sealed record AssetCard(
    AssetSummary Asset,
    int OpenFaults,
    IReadOnlyList<WorkOrderTask> MyOpenTasks,
    IReadOnlyList<AssetCardEvent> RecentEvents);

public sealed record AssetCardEvent(DateTimeOffset At, string Action, string ActorName);
