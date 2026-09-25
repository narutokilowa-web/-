using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;
using Mfop.Kernel.StateMachines;

namespace Mfop.Access.Domain;

public static class DeviceStates
{
    public const string PendingApproval = "PENDING_APPROVAL", Active = "ACTIVE", Rejected = "REJECTED", Revoked = "REVOKED";
}

public static class DeviceCommands
{
    public const string RegisterDevice = nameof(RegisterDevice);
    public const string ApproveDevice = nameof(ApproveDevice);
    public const string RejectDevice = nameof(RejectDevice);
    public const string RevokeDevice = nameof(RevokeDevice);
}

/// <summary>Transition tables. MUST equal docs/state-machines/device.yaml (StateMachineConformanceTests).</summary>
public static class AccessStateMachines
{
    public static readonly StateMachine Device = new(
        "Device",
        [DeviceStates.PendingApproval, DeviceStates.Active, DeviceStates.Rejected, DeviceStates.Revoked],
        DeviceStates.PendingApproval,
        [DeviceStates.Rejected, DeviceStates.Revoked],
        [
            new("D1", [StateMachine.Start], DeviceCommands.RegisterDevice, DeviceStates.PendingApproval, ["system"]),
            new("D2", [DeviceStates.PendingApproval], DeviceCommands.ApproveDevice, DeviceStates.Active, ["ADMIN", "SUPERVISOR"]),
            new("D3", [DeviceStates.PendingApproval], DeviceCommands.RejectDevice, DeviceStates.Rejected, ["ADMIN", "SUPERVISOR"]),
            new("D4", [DeviceStates.Active], DeviceCommands.RevokeDevice, DeviceStates.Revoked, ["ADMIN", "SUPERVISOR"]),
        ]);
}

/// <summary>
/// A personal phone (BYOD) registered by its owner. It can act only while ACTIVE; revocation is server-side only
/// (docs/lean-pilot/05 §5.6). Nothing on the phone is managed remotely.
/// </summary>
public sealed class Device : IAuditable
{
    public const string ReasonSuperseded = "SUPERSEDED_BY_NEW_REGISTRATION";
    public const string ReasonReplaced = "REPLACED";
    public const string ReasonUserDisabled = "USER_DISABLED";

    private Device() { }

    public Device(Guid id, Guid userId, string platform, string model, string osVersion, string appVersion, DateTimeOffset requestedAt,
        string? publicKey = null)
    {
        PublicKey = publicKey;
        Id = id;
        UserId = userId;
        Platform = platform;
        Model = model;
        OsVersion = osVersion;
        AppVersion = appVersion;
        Status = AccessStateMachines.Device.Create(DeviceCommands.RegisterDevice).Value.To;
        RequestedAt = requestedAt;
    }

    public string AuditEntityType => "device";

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string OsVersion { get; private set; } = string.Empty;
    public string AppVersion { get; private set; } = string.Empty;

    /// <summary>
    /// The phone's signing key (M4, docs/lean-pilot/05 §5.3–5.4): an EC P-256 public key (X.509 SubjectPublicKeyInfo, base64)
    /// whose private half lives in the Android Keystore and never leaves the phone. Null only for devices registered before M4,
    /// which cannot sign and therefore cannot push or obtain a grant.
    /// </summary>
    public string? PublicKey { get; private set; }
    public string Status { get; private set; } = DeviceStates.PendingApproval;
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionReason { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }
    public int Version { get; private set; }

    public bool IsActive => Status == DeviceStates.Active;

    /// <summary>D2 / D3 / D4 by a supervisor or admin. Nobody decides on their own device's approval.</summary>
    public Result<Transition> Decide(string command, Guid actorId, string? reason, DateTimeOffset now)
    {
        var transition = AccessStateMachines.Device.Resolve(Status, command);
        if (!transition.IsSuccess) return transition;
        if (command is DeviceCommands.ApproveDevice or DeviceCommands.RejectDevice && actorId == UserId)
        {
            return Error.Sod("A user cannot approve or reject their own device.");
        }

        if (command is DeviceCommands.RejectDevice or DeviceCommands.RevokeDevice && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("REASON_REQUIRED", "A reason is required.");
        }

        Status = transition.Value.To;
        if (command == DeviceCommands.RevokeDevice)
        {
            RevokedBy = actorId;
            RevokedAt = now;
            RevokeReason = reason!.Trim();
        }
        else
        {
            DecidedBy = actorId;
            DecidedAt = now;
            DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        }

        return transition;
    }

    /// <summary>D1 effect: a newer registration of the same user supersedes this pending one (system actor).</summary>
    public void RejectAsSuperseded(DateTimeOffset now)
    {
        Status = AccessStateMachines.Device.Resolve(Status, DeviceCommands.RejectDevice).Value.To;
        DecidedAt = now;
        DecisionReason = ReasonSuperseded;
    }

    /// <summary>D4 as an effect of U1 DisableUser (offboarding): every active device of the user is revoked.</summary>
    public void RevokeAsUserDisabled(Guid actorId, DateTimeOffset now)
    {
        Status = AccessStateMachines.Device.Resolve(Status, DeviceCommands.RevokeDevice).Value.To;
        RevokedBy = actorId;
        RevokedAt = now;
        RevokeReason = ReasonUserDisabled;
    }

    /// <summary>D2 effect: approving a new device revokes the user's previous active one (one active device per user).</summary>
    public void RevokeAsReplaced(Guid actorId, DateTimeOffset now)
    {
        Status = AccessStateMachines.Device.Resolve(Status, DeviceCommands.RevokeDevice).Value.To;
        RevokedBy = actorId;
        RevokedAt = now;
        RevokeReason = ReasonReplaced;
    }
}
