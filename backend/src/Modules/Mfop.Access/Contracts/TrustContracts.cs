namespace Mfop.Access.Contracts;

/// <summary>Headers of the device-trust protocol (docs/lean-pilot/05 §5.4, §5.7).</summary>
public static class TrustHttp
{
    /// <summary>Field-app version; below <c>policy.min_app_version</c> → 426 UPGRADE_REQUIRED.</summary>
    public const string AppVersionHeader = "X-App-Version";

    /// <summary>Phone monotonic clock (milliseconds since boot) and boot identifier, for trusted time.</summary>
    public const string ElapsedMsHeader = "X-Device-Elapsed-Ms";
    public const string BootIdHeader = "X-Device-Boot-Id";

    /// <summary>
    /// Device signature over <c>METHOD \n PATH \n TIMESTAMP \n NONCE \n hex(SHA-256(body))</c>, ECDSA P-256 / SHA-256, DER, base64,
    /// made with the Keystore key registered for the device. Required on <c>POST /sync/v1/push</c> and <c>POST /sync/v1/grant</c>.
    /// </summary>
    public const string SignatureHeader = "X-Device-Signature";
    public const string SignatureTimestampHeader = "X-Signature-Timestamp";
    public const string SignatureNonceHeader = "X-Signature-Nonce";

    public static string SignedMessage(string method, string path, string timestamp, string nonce, string bodySha256Hex) =>
        $"{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{nonce}\n{bodySha256Hex}";
}

public static class TrustErrorCodes
{
    public const string UpgradeRequired = "UPGRADE_REQUIRED";
    public const string DeviceKeyRequired = "DEVICE_KEY_REQUIRED";
    public const string SignatureRequired = "SIGNATURE_REQUIRED";
    public const string SignatureInvalid = "SIGNATURE_INVALID";
    public const string SignatureExpired = "SIGNATURE_EXPIRED";
    public const string NonceReused = "NONCE_REUSED";
    public const string GrantRequired = "GRANT_REQUIRED";
    public const string GrantInvalid = "GRANT_INVALID";
    public const string GrantExpired = "GRANT_EXPIRED";
    public const string GrantRevoked = "GRANT_REVOKED";
    public const string GrantWindowUnproven = "GRANT_WINDOW_UNPROVEN";
}

/// <summary>What the phone needs from the policies (sent with each grant).</summary>
public sealed record ClientPolicyDto(int OfflineGraceHours, int AppLockMinutes, string MinAppVersion);

/// <param name="SignedPayload">
/// The exact canonical JSON that <paramref name="Signature"/> covers. The phone verifies the signature over these bytes with the
/// server key it pinned for <paramref name="KeyId"/> and reads the grant from them.
/// </param>
public sealed record GrantDto(
    Guid GrantId, Guid UserId, Guid DeviceId, IReadOnlyList<string> Roles, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, int PolicyHours,
    string KeyId, string SignedPayload, string Signature, string PublicKey, ClientPolicyDto Policy, DateTimeOffset ServerTime);

public static class TimeConfidence
{
    /// <summary>Estimated from a clock sample taken on the same boot: server time + elapsed difference.</summary>
    public const string High = "HIGH";

    /// <summary>No sample for that boot: the phone's wall clock bounded by what the server knows.</summary>
    public const string Low = "LOW";
}

/// <param name="LastTrustedServerTime">Newest server time sampled for this device (lower bound known to the server).</param>
public sealed record TimeEstimate(DateTimeOffset Estimated, string Confidence, DateTimeOffset? LastTrustedServerTime);

/// <summary>Trusted time (docs/lean-pilot/05 §5.7).</summary>
public interface IDeviceClock
{
    Task<TimeEstimate> EstimateAsync(Guid deviceId, string? bootId, long? elapsedMs, DateTimeOffset? deviceTime, DateTimeOffset receivedAt, CancellationToken ct);
}

public static class GrantOutcomes
{
    public const string Ok = "OK", Reject = "REJECT", Quarantine = "QUARANTINE";
}

/// <param name="RaiseWarningFlag">A normal app cannot produce this result: flag it for the auditor (tampering or a bug).</param>
public sealed record GrantCheck(string Outcome, string? Code, string? Message, bool RaiseWarningFlag);

/// <summary>The server-side grant-window check of every pushed command (docs/lean-pilot/05 §5.5 table).</summary>
public interface IOfflineGrants
{
    Task<GrantCheck> CheckAsync(Guid? grantId, Guid userId, Guid deviceId, TimeEstimate time, DateTimeOffset receivedAt, CancellationToken ct);
}

/// <summary>Is this user ACTIVE and this device their ACTIVE device right now?</summary>
public interface IDeviceTrust
{
    Task<bool> IsActiveAsync(Guid userId, Guid deviceId, CancellationToken ct);
}

/// <summary>
/// Called inside the DisableUser unit of work (docs/lean-pilot/05 §5.6 steps 7–8), so everything is audited in one
/// transaction: e.g. open work orders flagged for reassignment, custody to reconcile.
/// </summary>
public interface IUserOffboardingHandler
{
    Task OnUserDisabledAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}

public sealed record PolicySettingDto(string Key, string Value, string Default, string Description, Guid? ChangedBy, DateTimeOffset ChangedAt, int Version);

public sealed record ChangePolicyRequest(string? Value, string? Reason);

public interface IPolicies
{
    Task<string> ValueAsync(string key, CancellationToken ct);

    Task<int> IntAsync(string key, CancellationToken ct);
}

public sealed record UserDeviceDto(Guid Id, string Model, string Status, DateTimeOffset RequestedAt, DateTimeOffset? RevokedAt, string? RevokeReason);

/// <param name="IdpPending">Identity-provider updates not yet delivered (disable / logout); <paramref name="IdpAlert"/> after repeated failures.</param>
public sealed record UserDto(
    Guid Id, string Username, string DisplayName, string Status, IReadOnlyList<string> Roles, DateTimeOffset? DisabledAt, string? DisabledByName,
    string? DisableReason, IReadOnlyList<UserDeviceDto> Devices, int IdpPending, bool IdpAlert, string? IdpLastError);

public sealed record UserDecisionRequest(string? Reason);
