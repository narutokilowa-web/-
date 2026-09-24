using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;

namespace Mfop.Assets.Domain;

/// <summary>Maker/checker workflow that creates an official asset (docs/lean-pilot/04 §4.4).</summary>
public sealed class Registration : IAuditable
{
    private Registration() { }

    public Registration(Guid id, Guid assetId, Guid registrarId, string reason, string? projectRef, string captureChannel, DateTimeOffset createdAt)
    {
        Id = id;
        AssetId = assetId;
        RegistrarId = registrarId;
        Reason = reason;
        ProjectRef = projectRef;
        CaptureChannel = captureChannel;
        State = RegistrationStates.Draft;
        CreatedAt = createdAt;
    }

    public string AuditEntityType => "registration";

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public Guid RegistrarId { get; private set; }
    public string Reason { get; private set; } = RegistrationReasons.BaselineSurvey;
    public string? ProjectRef { get; private set; }
    public string CaptureChannel { get; private set; } = "WEB";
    public string State { get; private set; } = RegistrationStates.Draft;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }
    public int ReturnCount { get; private set; }
    public int Version { get; private set; }

    public static Result<(string Reason, string? ProjectRef)> ValidateReason(string reason, string? projectRef)
    {
        if (!RegistrationReasons.All.Contains(reason))
        {
            return Error.Validation("INVALID_REASON", "Reason must be BASELINE_SURVEY, NEW_INSTALLATION or MISSED_IN_SURVEY.");
        }

        if (reason == RegistrationReasons.NewInstallation && string.IsNullOrWhiteSpace(projectRef))
        {
            return Error.Validation("PROJECT_REF_REQUIRED", "A project reference is required for a new installation.");
        }

        if (projectRef is { Length: > 100 })
        {
            return Error.Validation("INVALID_PROJECT_REF", "Project reference is at most 100 characters.");
        }

        return (reason, projectRef);
    }

    public Result Transition(string command, Guid actorId, bool actorIsApprover, DateTimeOffset now, string? note = null)
    {
        var resolved = AssetStateMachines.Registration.Resolve(State, command);
        if (!resolved.IsSuccess)
        {
            return resolved.Error!;
        }

        var transition = resolved.Value;
        if (transition.Actors.Contains("registrar") && actorId != RegistrarId)
        {
            return Error.Forbidden("NOT_REGISTRAR", "Only the registrar of this registration can do this.");
        }

        if (transition.Actors.Contains("APPROVER"))
        {
            if (!actorIsApprover)
            {
                return Error.Forbidden("FORBIDDEN", "Only an approver can decide a registration.");
            }

            if (actorId == RegistrarId)
            {
                return Error.Sod("The registrar of a registration cannot decide it (maker ≠ checker).");
            }

            DecidedBy = actorId;
            DecidedAt = now;
            DecisionNote = note;
        }

        if (command == AssetCommands.SubmitRegistration)
        {
            SubmittedAt = now;
        }

        if (command == AssetCommands.ReturnRegistration)
        {
            ReturnCount++;
        }

        State = transition.To;
        return Result.Success();
    }

    public void UpdateReason(string reason, string? projectRef)
    {
        Reason = reason;
        ProjectRef = projectRef;
    }
}
