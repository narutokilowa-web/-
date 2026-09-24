using System.Text.Json;
using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Faults.Domain;
using Mfop.Faults.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Faults.Application;

/// <summary>Fault commands (docs/state-machines/fault.yaml). Each runs in one unit of work with its audit events.</summary>
public sealed class FaultService(
    FaultsDbContext db,
    IUnitOfWork uow,
    IAuditTrail audit,
    TimeProvider clock,
    IAssetDirectory assets,
    IAssetStatusEvents assetStatus)
{
    public const string ResultCreated = "CREATED";
    public const string ResultConfirmedExisting = "CONFIRMED_EXISTING";

    /// <summary>F1 (new fault) or F2/F7 (duplicate → confirmation). Effect OS4: the light becomes FAULTY unless power was confirmed unavailable.</summary>
    public Task<Result<ReportFaultResult>> ReportAsync(CommandEnvelope cmd, ReportFaultRequest request, string captureChannel, CancellationToken ct) =>
        uow.ExecuteAsync(cmd, innerCt => ReportInCurrentUnitOfWorkAsync(cmd, request, captureChannel, innerCt), ct);

    /// <summary>The ReportFault rules without opening a unit of work (used by the web wrapper above and by the sync pipeline).</summary>
    public async Task<Result<ReportFaultResult>> ReportInCurrentUnitOfWorkAsync(CommandEnvelope cmd, ReportFaultRequest request, string captureChannel, CancellationToken innerCt)
    {
        if (!cmd.Actor.HasRole(RoleCodes.Supervisor))
        {
            return Error.Forbidden(ErrorCodes.Forbidden, "Only a supervisor can report faults in DEMO 1.");
        }

        var power = new PowerObservation(request.PowerAtInspection, request.PowerInfoSource, NullIfBlank(request.PowerNote));
        var invalid = Validate(request, power);
        if (invalid is not null) return invalid;

        var asset = await assets.GetAsync(request.AssetId, innerCt);
        if (asset is null) return Error.NotFound("ASSET_NOT_FOUND", "Asset not found.");
        if (asset.LifecycleState != AssetLifecycle.Active) return Error.Validation("ASSET_NOT_ACTIVE", "Faults can only be reported on an ACTIVE asset.");

        var now = clock.UtcNowMicros();
        var actorId = cmd.Actor.UserId!.Value;
        var description = NullIfBlank(request.Description);
        var existing = await db.Faults.SingleOrDefaultAsync(
            f => f.AssetId == asset.Id && f.Category == request.Category && (f.State == FaultStates.Open || f.State == FaultStates.InWorkOrder), innerCt);

        Guid faultId;
        string result;
        if (existing is not null)
        {
            var confirmed = existing.Confirm();
            if (!confirmed.IsSuccess) return confirmed.Error!;
            var confirmation = new FaultConfirmation(Ids.New(), existing.Id, actorId, now, power, description, captureChannel);
            db.Confirmations.Add(confirmation);
            audit.Record(existing, "fault.confirmed", extra: new Dictionary<string, object?> { ["transition"] = confirmed.Value.Id });
            audit.Record(confirmation, "fault_confirmation.recorded");
            (faultId, result) = (existing.Id, ResultConfirmedExisting);
        }
        else
        {
            var source = request.Source ?? (captureChannel == "WEB" ? "SUPERVISOR" : "FIELD_STAFF");
            var fault = new Fault(Ids.New(), asset.Id, request.Category, source, actorId, now, power, asset.ResponsibleParty, asset.ResponsibleEntityName, description, captureChannel);
            db.Faults.Add(fault);
            audit.Record(fault, "fault.reported");
            (faultId, result) = (fault.Id, ResultCreated);
        }

        if (request.Category != FaultVocabulary.CategoryOther && power.Power != FaultVocabulary.PowerUnavailable)
        {
            await assetStatus.ApplyAsync(asset.Id, AssetStatusCommands.OnFaultReported, "FAULT", faultId, innerCt);
        }

        return new ReportFaultResult(faultId, result);
    }

    public Task<Result<Guid>> DismissAsync(CommandEnvelope cmd, Guid faultId, DismissFaultRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var fault = await db.Faults.SingleOrDefaultAsync(f => f.Id == faultId, innerCt);
            if (fault is null) return Error.NotFound("FAULT_NOT_FOUND", "Fault not found.");
            var dismissed = fault.Dismiss(request.Reason, request.Note, cmd.Actor.UserId!.Value, clock.UtcNowMicros());
            if (!dismissed.IsSuccess) return dismissed.Error!;
            audit.Record(fault, "fault.dismissed", request.Note ?? request.Reason);
            return fault.Id;
        }, ct);

    private static Error? Validate(ReportFaultRequest r, PowerObservation power)
    {
        var details = new Dictionary<string, string>();
        if (r.Category is null || !FaultVocabulary.Categories.Contains(r.Category)) details["category"] = "unknown category";
        if (r.Source is not null && !FaultVocabulary.Sources.Contains(r.Source)) details["source"] = "unknown source";
        if (r.Description is { Length: > 1000 }) details["description"] = "at most 1000 characters";
        if (power.Validate() is { Details: { } powerDetails })
        {
            foreach (var (k, v) in powerDetails) details[k] = v;
        }

        return details.Count == 0 ? null : Error.Validation("INVALID_FAULT", "Invalid fault report.", details);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>ReportFault through the single mobile write path (/sync/v1/push). Same handler and rules as the web.</summary>
public sealed class ReportFaultSyncHandler(FaultService faults) : ISyncCommandHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => "ReportFault";

    public string Permission => "fault.report";

    public async Task<Result<SyncCommandOutcome>> HandleAsync(CommandEnvelope command, JsonElement payload, CancellationToken ct)
    {
        ReportFaultRequest? request;
        try
        {
            request = payload.Deserialize<ReportFaultRequest>(Json);
        }
        catch (JsonException)
        {
            request = null;
        }

        if (request is null)
        {
            return Error.Validation("INVALID_PAYLOAD", "ReportFault payload is not valid.");
        }

        var result = await faults.ReportInCurrentUnitOfWorkAsync(command, request, "MOBILE", ct);
        return result.IsSuccess ? new SyncCommandOutcome(result.Value.FaultId, result.Value.Result) : result.Error!;
    }
}

/// <summary>F4 / F5, called by the WorkOrders module inside its unit of work.</summary>
public sealed class FaultWorkflow(FaultsDbContext db, IAuditTrail audit) : IFaultWorkflow
{
    public async Task<FaultForWorkOrder?> GetAsync(Guid faultId, CancellationToken ct) =>
        (await GetManyAsync([faultId], ct)).GetValueOrDefault(faultId);

    public async Task<IReadOnlyDictionary<Guid, FaultForWorkOrder>> GetManyAsync(IReadOnlyCollection<Guid> faultIds, CancellationToken ct) =>
        await db.Faults.AsNoTracking()
            .Where(f => faultIds.Contains(f.Id))
            .Select(f => new FaultForWorkOrder(f.Id, f.AssetId, f.State, f.Category, f.Description, f.PowerAtInspection))
            .ToDictionaryAsync(f => f.Id, ct);

    public async Task<Result> OnWorkOrderCreatedAsync(Guid faultId, Guid workOrderId, CancellationToken ct)
    {
        var fault = await db.Faults.SingleAsync(f => f.Id == faultId, ct);
        var t = fault.OnWorkOrderCreated(workOrderId);
        if (!t.IsSuccess) return t.Error!;
        audit.Record(fault, "fault.in_work_order", extra: new Dictionary<string, object?> { ["work_order_id"] = workOrderId.ToString("D") });
        return Result.Success();
    }

    public async Task<Result> OnWorkOrderCancelledAsync(Guid faultId, CancellationToken ct)
    {
        var fault = await db.Faults.SingleAsync(f => f.Id == faultId, ct);
        var t = fault.OnWorkOrderCancelled();
        if (!t.IsSuccess) return t.Error!;
        audit.Record(fault, "fault.reopened_after_cancellation");
        return Result.Success();
    }

    public Task<int> CountOpenForAssetAsync(Guid assetId, CancellationToken ct) =>
        db.Faults.CountAsync(f => f.AssetId == assetId && (f.State == FaultStates.Open || f.State == FaultStates.InWorkOrder), ct);
}
