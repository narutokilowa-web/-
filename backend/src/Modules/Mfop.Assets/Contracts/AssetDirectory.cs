namespace Mfop.Assets.Contracts;

/// <summary>What other modules may know about a street light.</summary>
public sealed record AssetSummary(
    Guid Id,
    string? AssetCode,
    string LifecycleState,
    string? OperationalStatus,
    string PowerSource,
    string ResponsibleParty,
    string? ResponsibleEntityName,
    string? AddressText,
    decimal Latitude,
    decimal Longitude);

public static class AssetLifecycle
{
    public const string Active = "ACTIVE";
}

/// <summary>Operational-status events other modules may raise (docs/state-machines/asset-operational-status.yaml).</summary>
public static class AssetStatusCommands
{
    public const string OnFaultReported = "OnFaultReported";
    public const string OnWorkStarted = "OnWorkStarted";
    public const string OnWorkOrderClosedTestPassed = "OnWorkOrderClosedTestPassed";
    public const string OnWorkOrderClosedNotTested = "OnWorkOrderClosedNotTested";
}

/// <param name="TransitionId">e.g. OS4.</param>
public sealed record AssetStatusChange(string TransitionId, string? From, string To);

public interface IAssetDirectory
{
    Task<AssetSummary?> GetAsync(Guid assetId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, AssetSummary>> GetManyAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct);

    /// <summary>Resolves a scanned or typed tag (human code, bare code or QR URL) to the bound asset.</summary>
    Task<Guid?> ResolveTagAsync(string token, CancellationToken ct);

    /// <summary>Newest audit-history entries of the asset (for the compact card on the phone).</summary>
    Task<IReadOnlyList<HistoryEntry>> RecentHistoryAsync(Guid assetId, int take, CancellationToken ct);

    /// <summary>Dashboard: ACTIVE street lights by operational status.</summary>
    Task<IReadOnlyDictionary<string, int>> CountActiveByOperationalStatusAsync(CancellationToken ct);
}

/// <summary>
/// Applies an operational-status event inside the caller's unit of work (same transaction, audited).
/// Returns null when the status machine does not change the status from its current value.
/// </summary>
public interface IAssetStatusEvents
{
    Task<AssetStatusChange?> ApplyAsync(Guid assetId, string statusCommand, string causeType, Guid causeId, CancellationToken ct);
}

/// <summary>
/// Implemented by other modules (Faults, WorkOrders) so the asset history timeline includes their audit events
/// about the asset without the Assets module depending on them.
/// </summary>
public interface IAssetRelatedHistory
{
    Task<IReadOnlyList<(string EntityType, string EntityId)>> RelatedAuditEntitiesAsync(Guid assetId, CancellationToken ct);
}
