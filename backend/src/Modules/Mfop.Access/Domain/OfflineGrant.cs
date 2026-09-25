using Mfop.Kernel.Auditing;

namespace Mfop.Access.Domain;

public static class GrantStates
{
    public const string Valid = "VALID", Expired = "EXPIRED", Revoked = "REVOKED";
}

public static class GrantRevokeReasons
{
    public const string NewGrant = "NEW_GRANT", DeviceRevoked = "DEVICE_REVOKED", UserDisabled = "USER_DISABLED";
}

/// <summary>
/// An offline grant (docs/lean-pilot/05 §5.5, 04 §4.13): permission to work offline on one ACTIVE device until
/// <see cref="ExpiresAt"/>. Issued online only; signed with the server's grant key; never edited (append-only).
/// Revocation is a separate append-only record (<see cref="GrantRevocation"/>); expiry is derived from the time.
/// </summary>
public sealed class OfflineGrant : IAuditable
{
    private OfflineGrant() { }

    public OfflineGrant(Guid id, Guid userId, Guid deviceId, string[] roles, DateTimeOffset issuedAt, int policyHours, string keyId, string signature)
    {
        Id = id;
        UserId = userId;
        DeviceId = deviceId;
        Roles = roles;
        IssuedAt = issuedAt;
        PolicyHours = policyHours;
        ExpiresAt = issuedAt.AddHours(policyHours);
        KeyId = keyId;
        Signature = signature;
    }

    public string AuditEntityType => "offline_grant";

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string[] Roles { get; private set; } = [];
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int PolicyHours { get; private set; }
    public string KeyId { get; private set; } = string.Empty;
    public string Signature { get; private set; } = string.Empty;
}

/// <summary>G2/G3 of 04 §4.13: a grant revoked by a newer grant, a device revocation or a user disable. Append-only.</summary>
public sealed class GrantRevocation : IAuditable
{
    private GrantRevocation() { }

    public GrantRevocation(Guid id, Guid grantId, string reason, Guid? revokedBy, DateTimeOffset revokedAt)
    {
        Id = id;
        GrantId = grantId;
        Reason = reason;
        RevokedBy = revokedBy;
        RevokedAt = revokedAt;
    }

    public string AuditEntityType => "offline_grant_revocation";

    public Guid Id { get; private set; }
    public Guid GrantId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid? RevokedBy { get; private set; }
    public DateTimeOffset RevokedAt { get; private set; }
}
