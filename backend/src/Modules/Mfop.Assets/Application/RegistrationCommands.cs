using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Assets.Domain;
using Mfop.Assets.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mfop.Assets.Application;

/// <summary>
/// Registration commands (maker/checker). Every command runs in the unit of work: state change + audit events
/// commit together; old/new values are captured automatically.
/// </summary>
public sealed class RegistrationCommands(
    AssetsDbContext db,
    IUnitOfWork uow,
    IAuditTrail audit,
    TimeProvider clock,
    IOptions<TagOptions> tagOptions)
{
    public const string CaptureChannelWeb = "WEB";

    public Task<Result<Guid>> DraftAsync(CommandEnvelope cmd, RegistrationRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, innerCt =>
        {
            var actor = cmd.Actor;
            if (!actor.HasRole(RoleCodes.Registrar) || AssetStateMachines.Registration.Create(AssetCommands.RegisterAssetDraft) is not { IsSuccess: true })
            {
                return Task.FromResult(Result<Guid>.Failure(Error.Forbidden("FORBIDDEN", "Only a registrar can register assets.")));
            }

            var reason = Registration.ValidateReason(request.Reason, request.ProjectRef);
            if (!reason.IsSuccess) return Task.FromResult(Result<Guid>.Failure(reason.Error!));
            var attributes = request.Attributes.Validate();
            if (!attributes.IsSuccess) return Task.FromResult(Result<Guid>.Failure(attributes.Error!));

            var now = clock.UtcNowMicros();
            var asset = new StreetLight(Ids.New(), attributes.Value, CaptureChannelWeb, now);
            var registration = new Registration(Ids.New(), asset.Id, actor.UserId!.Value, request.Reason, request.ProjectRef, CaptureChannelWeb, now);
            db.StreetLights.Add(asset);
            db.Registrations.Add(registration);
            audit.Record(registration, "registration.drafted");
            audit.Record(asset, "street_light.created");
            return Task.FromResult(Result<Guid>.Ok(registration.Id));
        }, ct);

    public Task<Result<Guid>> UpdateDraftAsync(CommandEnvelope cmd, Guid registrationId, RegistrationRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var (registration, asset) = await LoadAsync(registrationId, innerCt);
            if (registration is null) return NotFound();

            var reason = Registration.ValidateReason(request.Reason, request.ProjectRef);
            if (!reason.IsSuccess) return reason.Error!;
            var attributes = request.Attributes.Validate();
            if (!attributes.IsSuccess) return attributes.Error!;

            var transitioned = registration.Transition(AssetCommands.UpdateRegistrationDraft, cmd.Actor.UserId!.Value, false, clock.UtcNowMicros());
            if (!transitioned.IsSuccess) return transitioned.Error!;

            registration.UpdateReason(request.Reason, request.ProjectRef);
            asset!.Apply(attributes.Value);
            audit.Record(registration, "registration.updated");
            audit.Record(asset, "street_light.attributes_updated");
            return registration.Id;
        }, ct);

    public Task<Result<Guid>> SubmitAsync(CommandEnvelope cmd, Guid registrationId, CancellationToken ct) =>
        TransitionAsync(cmd, registrationId, AssetCommands.SubmitRegistration, "registration.submitted", null, (_, _) => { }, ct);

    public Task<Result<Guid>> DiscardAsync(CommandEnvelope cmd, Guid registrationId, CancellationToken ct) =>
        TransitionAsync(cmd, registrationId, AssetCommands.DiscardDraft, "registration.discarded", null, (_, asset) =>
        {
            asset.RejectLifecycle();
            audit.Record(asset, "street_light.registration_discarded");
        }, ct);

    public Task<Result<Guid>> ReturnAsync(CommandEnvelope cmd, Guid registrationId, string? note, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(note)
            ? Task.FromResult(Result<Guid>.Failure(Error.Validation("NOTE_REQUIRED", "Explain what must be corrected.")))
            : TransitionAsync(cmd, registrationId, AssetCommands.ReturnRegistration, "registration.returned", note, (_, _) => { }, ct);

    public Task<Result<Guid>> RejectAsync(CommandEnvelope cmd, Guid registrationId, string? note, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(note)
            ? Task.FromResult(Result<Guid>.Failure(Error.Validation("NOTE_REQUIRED", "A reason is required to reject.")))
            : TransitionAsync(cmd, registrationId, AssetCommands.RejectRegistration, "registration.rejected", note, (_, asset) =>
            {
                asset.RejectLifecycle();
                audit.Record(asset, "street_light.registration_rejected");
            }, ct);

    /// <summary>R5: approval by a user other than the registrar → asset code, generated tag, version 1 snapshot.</summary>
    public Task<Result<Guid>> ApproveAsync(CommandEnvelope cmd, Guid registrationId, string? note, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var (registration, asset) = await LoadAsync(registrationId, innerCt);
            if (registration is null) return NotFound();

            var now = clock.UtcNowMicros();
            var actorId = cmd.Actor.UserId!.Value;
            var transitioned = registration.Transition(AssetCommands.ApproveRegistration, actorId, cmd.Actor.HasRole(RoleCodes.Approver), now, note);
            if (!transitioned.IsSuccess) return transitioned.Error!;

            var key = tagOptions.Value.CheckKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Tags:CheckKey is not configured.");
            }

            var number = await db.Database.SqlQuery<long>($"SELECT nextval('assets.street_light_code_seq') AS \"Value\"").SingleAsync(innerCt);
            var assetCode = $"SL-{number:D6}";
            var code = TagCodes.NewCode();
            var tag = new Tag(Ids.New(), code, TagCodes.CheckCode(code, key), asset!.Id, now);
            db.Tags.Add(tag);

            asset.Activate(assetCode, tag.Id, versionNo: 1);
            var snapshot = AssetSnapshot.Build(asset, tag.Code);
            var snapshotHash = AssetSnapshot.Hash(snapshot);
            var version = new AssetVersion(Ids.New(), asset.Id, 1, AssetSnapshot.Serialize(snapshot), snapshotHash, actorId, now, "REGISTRATION_APPROVED");
            db.AssetVersions.Add(version);

            audit.Record(registration, "registration.approved", note);
            audit.Record(asset, "street_light.activated");
            audit.Record(tag, "tag.generated_and_bound");
            audit.Record(version, "asset_version.created", extra: new Dictionary<string, object?> { ["snapshot_hash"] = snapshotHash });
            return registration.Id;
        }, ct);

    private Task<Result<Guid>> TransitionAsync(CommandEnvelope cmd, Guid registrationId, string command, string action, string? note, Action<Registration, StreetLight> effects, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var (registration, asset) = await LoadAsync(registrationId, innerCt);
            if (registration is null) return NotFound();

            var transitioned = registration.Transition(command, cmd.Actor.UserId!.Value, cmd.Actor.HasRole(RoleCodes.Approver), clock.UtcNowMicros(), note);
            if (!transitioned.IsSuccess) return transitioned.Error!;

            if (command == AssetCommands.SubmitRegistration)
            {
                var valid = asset!.ToAttributes().Validate();
                if (!valid.IsSuccess) return valid.Error!;
            }

            audit.Record(registration, action, note);
            effects(registration, asset!);
            return registration.Id;
        }, ct);

    private async Task<(Registration? Registration, StreetLight? Asset)> LoadAsync(Guid registrationId, CancellationToken ct)
    {
        var registration = await db.Registrations.SingleOrDefaultAsync(r => r.Id == registrationId, ct);
        if (registration is null) return (null, null);
        var asset = await db.StreetLights.SingleAsync(a => a.Id == registration.AssetId, ct);
        return (registration, asset);
    }

    private static Error NotFound() => Error.NotFound("REGISTRATION_NOT_FOUND", "Registration not found.");
}
