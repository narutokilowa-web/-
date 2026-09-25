using Mfop.Kernel.StateMachines;

namespace Mfop.Assets.Domain;

public static class RegistrationStates
{
    public const string Draft = "DRAFT", Submitted = "SUBMITTED", Returned = "RETURNED", Approved = "APPROVED", Rejected = "REJECTED", Discarded = "DISCARDED";
}

public static class LifecycleStates
{
    public const string Pending = "PENDING", Active = "ACTIVE", Rejected = "REJECTED";
}

public static class OperationalStatuses
{
    public const string Unknown = "UNKNOWN", Operational = "OPERATIONAL", Faulty = "FAULTY", UnderMaintenance = "UNDER_MAINTENANCE";
}

public static class AssetCommands
{
    public const string RegisterAssetDraft = nameof(RegisterAssetDraft);
    public const string DiscardDraft = nameof(DiscardDraft);
    public const string SubmitRegistration = nameof(SubmitRegistration);
    public const string ReturnRegistration = nameof(ReturnRegistration);
    public const string ApproveRegistration = nameof(ApproveRegistration);
    public const string RejectRegistration = nameof(RejectRegistration);
    public const string UpdateRegistrationDraft = nameof(UpdateRegistrationDraft);
}

/// <summary>
/// Transition tables. MUST equal docs/state-machines/{registration,asset-lifecycle,asset-operational-status}.yaml
/// (StateMachineConformanceTests). Change the YAML and this file in the same PR.
/// </summary>
public static class AssetStateMachines
{
    private const string Start = StateMachine.Start;

    public static readonly StateMachine Registration = new(
        "Registration",
        [RegistrationStates.Draft, RegistrationStates.Submitted, RegistrationStates.Returned, RegistrationStates.Approved, RegistrationStates.Rejected, RegistrationStates.Discarded],
        RegistrationStates.Draft,
        [RegistrationStates.Approved, RegistrationStates.Rejected, RegistrationStates.Discarded],
        [
            new("R1", [Start], AssetCommands.RegisterAssetDraft, RegistrationStates.Draft, ["REGISTRAR"]),
            new("R2", [RegistrationStates.Draft], AssetCommands.DiscardDraft, RegistrationStates.Discarded, ["registrar"]),
            new("R3", [RegistrationStates.Draft, RegistrationStates.Returned], AssetCommands.SubmitRegistration, RegistrationStates.Submitted, ["registrar"]),
            new("R4", [RegistrationStates.Submitted], AssetCommands.ReturnRegistration, RegistrationStates.Returned, ["APPROVER"]),
            new("R5", [RegistrationStates.Submitted], AssetCommands.ApproveRegistration, RegistrationStates.Approved, ["APPROVER"]),
            new("R6", [RegistrationStates.Submitted], AssetCommands.RejectRegistration, RegistrationStates.Rejected, ["APPROVER"]),
            new("R7", [RegistrationStates.Draft], AssetCommands.UpdateRegistrationDraft, RegistrationStates.Draft, ["registrar"]),
            new("R8", [RegistrationStates.Returned], AssetCommands.UpdateRegistrationDraft, RegistrationStates.Returned, ["registrar"]),
        ]);

    public static readonly StateMachine AssetLifecycle = new(
        "AssetLifecycle",
        [LifecycleStates.Pending, LifecycleStates.Active, LifecycleStates.Rejected],
        LifecycleStates.Pending,
        [LifecycleStates.Rejected],
        [
            new("AL1", [Start], "RegisterAssetDraft", LifecycleStates.Pending, ["system"]),
            new("AL2", [LifecycleStates.Pending], "OnRegistrationApproved", LifecycleStates.Active, ["system"]),
            new("AL3", [LifecycleStates.Pending], "OnRegistrationRejectedOrDiscarded", LifecycleStates.Rejected, ["system"]),
        ]);

    public static readonly StateMachine AssetOperationalStatus = new(
        "AssetOperationalStatus",
        [OperationalStatuses.Unknown, OperationalStatuses.Operational, OperationalStatuses.Faulty, OperationalStatuses.UnderMaintenance],
        OperationalStatuses.Unknown,
        [],
        [
            new("OS1", [Start], "OnRegistrationApproved", OperationalStatuses.Unknown, ["system"]),
            new("OS2", [Start], "OnRegistrationApprovedObservedOperational", OperationalStatuses.Operational, ["system"]),
            new("OS3", [Start], "OnRegistrationApprovedObservedFaulty", OperationalStatuses.Faulty, ["system"]),
            new("OS4", [OperationalStatuses.Operational, OperationalStatuses.Unknown], "OnFaultReported", OperationalStatuses.Faulty, ["system"]),
            new("OS5", [OperationalStatuses.Operational, OperationalStatuses.Unknown, OperationalStatuses.Faulty], "OnWorkStarted", OperationalStatuses.UnderMaintenance, ["system"]),
            new("OS6", [OperationalStatuses.UnderMaintenance], "OnWorkOrderClosedTestPassed", OperationalStatuses.Operational, ["system"]),
            new("OS7", [OperationalStatuses.UnderMaintenance], "OnWorkOrderClosedNotTested", OperationalStatuses.Unknown, ["system"]),
        ]);
}
