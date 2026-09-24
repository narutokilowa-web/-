namespace Mfop.Access.Contracts;

public static class DeviceHttp
{
    /// <summary>Header carrying the phone's device id on every mobile request (docs/lean-pilot/05 §5.4).</summary>
    public const string DeviceIdHeader = "X-Device-Id";
}

/// <summary>Stable codes returned when a request's device is not allowed to act.</summary>
public static class DeviceErrorCodes
{
    public const string DeviceRequired = "DEVICE_REQUIRED";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string DeviceNotApproved = "DEVICE_NOT_APPROVED";
    public const string DeviceRejected = "DEVICE_REJECTED";
    public const string DeviceRevoked = "DEVICE_REVOKED";
}

public sealed record RegisterDeviceRequest(string Platform, string Model, string OsVersion, string AppVersion);

public sealed record DeviceDecisionRequest(string? Reason);

public sealed record DeviceStatusDto(Guid DeviceId, string Status, string? Reason);

public sealed record DeviceDto(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    string Username,
    string Platform,
    string Model,
    string OsVersion,
    string AppVersion,
    string Status,
    DateTimeOffset RequestedAt,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    string? RevokedByName,
    DateTimeOffset? RevokedAt,
    string? RevokeReason,
    DateTimeOffset? LastPullAt,
    DateTimeOffset? LastPushAt,
    int? LastReportedPendingCommands);

/// <param name="HasActiveDevice">Work can only be assigned to a technician with an ACTIVE device (W2).</param>
public sealed record AssignableTechnician(Guid UserId, string DisplayName, string Username, bool HasActiveDevice);

public sealed record AssigneeCheck(bool Exists, bool IsActive, bool IsTechnician, bool HasActiveDevice);

/// <summary>Who can receive work orders. Read from our DB (roles, user status, device status).</summary>
public interface IAssigneeDirectory
{
    Task<IReadOnlyList<AssignableTechnician>> TechniciansAsync(CancellationToken ct);

    Task<AssigneeCheck> CheckAsync(Guid userId, CancellationToken ct);
}

public sealed record DeviceSyncSnapshot(DateTimeOffset? LastPullAt, DateTimeOffset? LastPushAt, int? LastReportedPendingCommands);

/// <summary>Last sync activity per device, shown on the Devices page. Implemented by the Sync module.</summary>
public interface IDeviceSyncInfo
{
    Task<IReadOnlyDictionary<Guid, DeviceSyncSnapshot>> ForDevicesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);
}
