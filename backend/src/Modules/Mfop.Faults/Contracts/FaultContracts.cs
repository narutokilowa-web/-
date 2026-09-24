namespace Mfop.Faults.Contracts;

/// <param name="PowerAtInspection">CONFIRMED_AVAILABLE / CONFIRMED_UNAVAILABLE / UNKNOWN.</param>
public sealed record ReportFaultRequest(
    Guid AssetId,
    string Category,
    string PowerAtInspection,
    string? PowerInfoSource,
    string? PowerNote,
    string? Description,
    string? Source);

public sealed record DismissFaultRequest(string Reason, string? Note);

/// <param name="Result">CREATED, or CONFIRMED_EXISTING when the report was recorded as a confirmation of an open fault.</param>
public sealed record ReportFaultResult(Guid FaultId, string Result);

public sealed record FaultConfirmationDto(Guid Id, string ConfirmedByName, DateTimeOffset At, string PowerAtInspection, string? PowerInfoSource, string? Description);

public sealed record FaultDto(
    Guid Id,
    Guid AssetId,
    string? AssetCode,
    string? AssetAddress,
    string Category,
    string Source,
    Guid ReportedBy,
    string ReportedByName,
    DateTimeOffset ReportedAt,
    string PowerAtInspection,
    string? PowerInfoSource,
    string? PowerNote,
    bool ReportedWithoutPower,
    string ResponsibleParty,
    string? Description,
    string State,
    Guid? WorkOrderId,
    string? DismissReason,
    string? DismissNote,
    string? DismissedByName,
    DateTimeOffset? DismissedAt,
    int ConfirmationCount,
    IReadOnlyList<FaultConfirmationDto> Confirmations);

public sealed record FaultForWorkOrder(Guid Id, Guid AssetId, string State, string Category, string? Description, string PowerAtInspection);

/// <summary>Fault transitions driven by the WorkOrders module, inside the caller's unit of work (F4, F5).</summary>
public interface IFaultWorkflow
{
    Task<FaultForWorkOrder?> GetAsync(Guid faultId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, FaultForWorkOrder>> GetManyAsync(IReadOnlyCollection<Guid> faultIds, CancellationToken ct);

    Task<Mfop.Kernel.Results.Result> OnWorkOrderCreatedAsync(Guid faultId, Guid workOrderId, CancellationToken ct);

    Task<Mfop.Kernel.Results.Result> OnWorkOrderCancelledAsync(Guid faultId, CancellationToken ct);

    /// <summary>Open (OPEN or IN_WORK_ORDER) faults of an asset, for the phone's asset card.</summary>
    Task<int> CountOpenForAssetAsync(Guid assetId, CancellationToken ct);
}
