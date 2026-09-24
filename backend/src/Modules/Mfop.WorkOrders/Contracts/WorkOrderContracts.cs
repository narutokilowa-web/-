using Mfop.Assets.Contracts;

namespace Mfop.WorkOrders.Contracts;

public sealed record CreateWorkOrderRequest(Guid FaultId, string Priority, string? Note);

public sealed record AssignRequest(Guid AssigneeId, string? Reason);

public sealed record CancelRequest(string? Reason);

public sealed record AssignmentDto(Guid Id, Guid AssigneeId, string AssigneeName, string State, DateTimeOffset StartedAt, string StartedByName, DateTimeOffset? EndedAt, string? EndReason);

public sealed record WorkOrderListItem(
    Guid Id, string Code, string State, string Priority, Guid AssetId, string? AssetCode, string? AssetAddress, string FaultCategory,
    Guid? AssigneeId, string? AssigneeName, DateTimeOffset CreatedAt, DateTimeOffset? SlaDueAt);

public sealed record WorkOrderDto(
    Guid Id,
    string Code,
    string State,
    string Priority,
    DateTimeOffset? SlaDueAt,
    string? Note,
    Guid FaultId,
    string FaultCategory,
    string FaultState,
    AssetSummary? Asset,
    Guid? AssigneeId,
    string? AssigneeName,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string? CancelReason,
    DateTimeOffset? CancelledAt,
    IReadOnlyList<AssignmentDto> Assignments,
    IReadOnlyList<string> AllowedCommands);

/// <summary>A task on the technician's phone ("My tasks").</summary>
public sealed record WorkOrderTask(
    Guid Id, string Code, string State, string Priority, DateTimeOffset? SlaDueAt, string? Note, string FaultCategory,
    string? FaultDescription, string FaultPowerAtInspection, DateTimeOffset AssignedAt, AssetSummary Asset);

/// <summary>Read side for the Sync module (pull, tag card). Only the current assignee's non-terminal work orders.</summary>
public interface IWorkOrderTasks
{
    Task<IReadOnlyList<WorkOrderTask>> OpenTasksForAsync(Guid assigneeId, CancellationToken ct);
}

/// <summary>
/// Implemented from M3 by the Evidence and Inventory modules: W4 (Cancel) is allowed only while the work order has
/// no evidence and no material consumption. In M2 neither exists yet, so no implementation is registered.
/// </summary>
public interface IWorkOrderActivity
{
    Task<bool> HasEvidenceOrConsumptionAsync(Guid workOrderId, CancellationToken ct);
}
