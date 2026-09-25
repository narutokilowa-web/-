using Mfop.Kernel.Auditing;

namespace Mfop.Access.Domain;

/// <summary>
/// A request-signature nonce already used by a device (docs/lean-pilot/05 §5.4: nonces are single-use). Technical
/// replay protection, not business state (audit-exempt). Old rows may be purged after the signature time window.
/// </summary>
public sealed class DeviceNonce : IAuditExempt
{
    private DeviceNonce() { }

    public DeviceNonce(Guid deviceId, string nonce, DateTimeOffset usedAt)
    {
        DeviceId = deviceId;
        Nonce = nonce;
        UsedAt = usedAt;
    }

    public Guid DeviceId { get; private set; }
    public string Nonce { get; private set; } = string.Empty;
    public DateTimeOffset UsedAt { get; private set; }
}

/// <summary>
/// Trusted-time sample (docs/lean-pilot/05 §5.7): at <see cref="ServerTime"/> the phone's monotonic clock read
/// <see cref="ElapsedMs"/> since boot <see cref="BootId"/>. The latest sample per (device, boot) is kept. Telemetry (audit-exempt).
/// </summary>
public sealed class DeviceClockSample : IAuditExempt
{
    private DeviceClockSample() { }

    public DeviceClockSample(Guid deviceId, string bootId, DateTimeOffset serverTime, long elapsedMs)
    {
        DeviceId = deviceId;
        BootId = bootId;
        ServerTime = serverTime;
        ElapsedMs = elapsedMs;
    }

    public Guid DeviceId { get; private set; }
    public string BootId { get; private set; } = string.Empty;
    public DateTimeOffset ServerTime { get; private set; }
    public long ElapsedMs { get; private set; }

    public void Update(DateTimeOffset serverTime, long elapsedMs)
    {
        ServerTime = serverTime;
        ElapsedMs = elapsedMs;
    }
}

public static class IdpTaskKinds
{
    public const string DisableUser = "DISABLE_USER", EnableUser = "ENABLE_USER", LogoutAll = "LOGOUT_ALL", LogoutMobile = "LOGOUT_MOBILE";
}

/// <summary>
/// Outbox for the identity provider (Keycloak): written in the same transaction as the offboarding or revocation, sent after
/// commit with retry (docs/lean-pilot/05 §5.6 step 9). Our own checks already block access, so a delay here is not a gap.
/// Technical delivery record (audit-exempt); the decision it carries out is audited.
/// </summary>
public sealed class IdpTask : IAuditExempt
{
    private IdpTask() { }

    public IdpTask(Guid id, string kind, Guid userId, string idpSubject, DateTimeOffset createdAt)
    {
        Id = id;
        Kind = kind;
        UserId = userId;
        IdpSubject = idpSubject;
        CreatedAt = createdAt;
        NextAttemptAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public string IdpSubject { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? DoneAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Set after <see cref="AlertAfterAttempts"/> failures: shown to admins as an operational alert.</summary>
    public bool Alert { get; private set; }

    public const int AlertAfterAttempts = 5;

    public void Succeeded(DateTimeOffset now)
    {
        Attempts++;
        DoneAt = now;
        LastError = null;
    }

    public void Failed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        NextAttemptAt = now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Min(Attempts, 10))));
        if (Attempts >= AlertAfterAttempts) Alert = true;
    }
}
