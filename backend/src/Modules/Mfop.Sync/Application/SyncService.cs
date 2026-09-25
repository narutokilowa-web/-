using System.Security.Cryptography;
using System.Text;
using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Http;
using Mfop.Inventory.Contracts;
using Mfop.Rules.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Sync.Contracts;
using Mfop.Sync.Domain;
using Mfop.Sync.Infrastructure;
using Mfop.WorkOrders.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Sync.Application;

/// <summary>
/// The single write path for phones (docs/lean-pilot/01 §1.4): an ordered, device-signed batch of commands, one result per
/// command. Each command is authorized by its own permission action, placed on the server's clock (trusted time, 05 §5.7),
/// checked against the offline grant it was made under (05 §5.5), and runs in its own unit of work together with its receipt,
/// so a command id is applied at most once (idempotency). Commands whose time cannot be proven inside the grant are
/// quarantined for a supervisor; commands built on a stale view of a work order are refused by the handlers (WORK_ORDER_CHANGED).
/// </summary>
public sealed class SyncService(
    SyncDbContext db,
    IUnitOfWork uow,
    Mfop.Kernel.Auditing.IAuditTrail audit,
    IEnumerable<ISyncCommandHandler> handlers,
    TimeProvider clock,
    IUserDirectory users,
    IWorkOrderTasks tasks,
    IAssetDirectory assets,
    IFaultWorkflow faults,
    IWorkOrderReference reference,
    IInventoryLedger inventory,
    IRecommendations recommendations,
    IDeviceClock deviceClock,
    IOfflineGrants grants,
    IPolicies policies,
    IFlags flags)
{
    /// <summary>docs/lean-pilot/05 §5.7: |device clock − trusted time| above this raises a CLOCK_SKEW flag (INFO).</summary>
    public static readonly TimeSpan ClockSkewThreshold = TimeSpan.FromMinutes(5);

    public const int MaxBatch = 100;

    public async Task<Result<PushResponse>> PushAsync(ActorContext actor, PushRequest request, string? correlationId, CancellationToken ct)
    {
        if (actor.DeviceId is not { } deviceId) return Error.Forbidden(DeviceErrorCodes.DeviceRequired, "Push requires an approved device.");
        var commands = request.Commands ?? [];
        if (commands.Count > MaxBatch) return Error.Validation("BATCH_TOO_LARGE", $"At most {MaxBatch} commands per push.");

        var results = new List<PushCommandResult>(commands.Count);
        var flagsToRaise = new List<(string Code, string Severity, Dictionary<string, object?> Explanation)>();
        foreach (var command in commands)
        {
            results.Add(await ProcessAsync(actor, deviceId, command, correlationId, flagsToRaise, ct));
        }

        if (flagsToRaise.Count > 0)
        {
            await RaiseFlagsAsync(actor, deviceId, flagsToRaise, ct);
        }

        var state = await DeviceStateAsync(deviceId, actor.UserId!.Value, ct);
        state.Pushed(clock.UtcNowMicros(), request.PendingCommands);
        await db.SaveChangesAsync(ct);
        return new PushResponse(clock.UtcNowMicros(), results);
    }

    public async Task<Result<PullResponse>> PullAsync(ActorContext actor, CancellationToken ct)
    {
        if (actor.DeviceId is not { } deviceId) return Error.Forbidden(DeviceErrorCodes.DeviceRequired, "Pull requires an approved device.");
        var userId = actor.UserId!.Value;
        var now = clock.UtcNowMicros();
        var open = await tasks.OpenTasksForAsync(userId, ct);
        var names = await users.DisplayNamesAsync([userId], ct);

        var state = await DeviceStateAsync(deviceId, userId, ct);
        state.Pulled(now);
        await db.SaveChangesAsync(ct);
        var custody = PermissionMatrix.Allows("stock.custody.read_own", actor.Roles) ? await inventory.CustodyOfAsync(userId, ct) : [];
        var stock = PermissionMatrix.Allows("stock.issue.respond", actor.Roles) ? await inventory.TechnicianStockAsync(userId, ct) : null;
        var policy = new ClientPolicyDto(await policies.IntAsync("offline_grace_hours", ct), await policies.IntAsync("app_lock_minutes", ct),
            await policies.ValueAsync("min_app_version", ct));
        return new PullResponse(now, now.ToString("O"), new PullUser(userId, names.GetValueOrDefault(userId, "?"), actor.Roles), deviceId, open,
            await reference.ReferenceAsync(ct), custody, await inventory.ItemsAsync(ct), policy, stock);
    }

    /// <summary>Online tag resolution for the Scan tab: the asset card (docs/lean-pilot/08 §8.3).</summary>
    public async Task<Result<AssetCard>> AssetCardAsync(ActorContext actor, string token, CancellationToken ct)
    {
        var assetId = await assets.ResolveTagAsync(token, ct);
        if (assetId is null) return Error.NotFound("TAG_NOT_FOUND", "No active asset for this tag.");
        var asset = await assets.GetAsync(assetId.Value, ct);
        var mine = actor.UserId is { } userId ? (await tasks.OpenTasksForAsync(userId, ct)).Where(t => t.Asset.Id == assetId).ToList() : [];
        var recent = await assets.RecentHistoryAsync(assetId.Value, 5, ct);
        return new AssetCard(
            asset!,
            await faults.CountOpenForAssetAsync(assetId.Value, ct),
            mine,
            recent.Select(e => new AssetCardEvent(e.OccurredAt, e.Action, e.ActorName)).ToList(),
            await recommendations.OpenForAssetAsync(assetId.Value, ct));
    }

    private async Task<PushCommandResult> ProcessAsync(ActorContext actor, Guid deviceId, PushCommand command, string? correlationId,
        List<(string Code, string Severity, Dictionary<string, object?> Explanation)> flagsToRaise, CancellationToken ct)
    {
        if (command.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(command.Type))
        {
            return new PushCommandResult(command.CommandId, SyncResults.Rejected, "INVALID_COMMAND", "commandId and type are required.", null);
        }

        var payload = command.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "{}" : command.Payload.GetRawText();
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

        var previous = await db.Commands.AsNoTracking().SingleOrDefaultAsync(c => c.CommandId == command.CommandId, ct);
        if (previous is not null)
        {
            return await ReplayAsync(previous, actor, deviceId, payloadHash, ct);
        }

        var receivedAt = clock.UtcNowMicros();
        var time = await deviceClock.EstimateAsync(deviceId, command.BootId, command.ElapsedMs, command.DeviceTime?.ToUniversalTime(), receivedAt, ct);
        var receipt = new ReceiptInfo(payload, payloadHash, command.GrantId, time);

        var handler = handlers.FirstOrDefault(h => h.Type == command.Type);
        if (handler is null)
        {
            return await RejectAsync(actor, deviceId, command, receipt, "UNKNOWN_COMMAND", $"Unknown command type '{command.Type}'.", ct);
        }

        if (!PermissionMatrix.Allows(handler.Permission, actor.Roles))
        {
            return await RejectAsync(actor, deviceId, command, receipt, ErrorCodesHttp.Forbidden, $"Your roles do not allow '{handler.Permission}'.", ct);
        }

        // Offline grant (docs/lean-pilot/05 §5.5): the user and device are ACTIVE (checked for the whole request); the command must
        // have been made inside the window of a grant of this device.
        var grant = await grants.CheckAsync(command.GrantId, actor.UserId!.Value, deviceId, time, receivedAt, ct);
        if (grant.RaiseWarningFlag)
        {
            flagsToRaise.Add((grant.Code == TrustErrorCodes.GrantExpired ? FlagCodes.GrantExpired : FlagCodes.GrantInvalid, "WARNING",
                new Dictionary<string, object?>
                {
                    ["command_id"] = command.CommandId.ToString("D"), ["command_type"] = command.Type,
                    ["grant_id"] = command.GrantId?.ToString("D"), ["estimated_time"] = time.Estimated.ToString("O"), ["time_confidence"] = time.Confidence,
                }));
        }

        if (grant.Outcome == GrantOutcomes.Reject)
        {
            return await RejectAsync(actor, deviceId, command, receipt, grant.Code!, grant.Message!, ct);
        }

        if (grant.Outcome == GrantOutcomes.Quarantine)
        {
            return await QuarantineAsync(actor, deviceId, command, receipt, grant.Code!, grant.Message!, correlationId, ct);
        }

        if (time.Confidence == TimeConfidence.High && command.DeviceTime is { } deviceTime
            && (deviceTime.ToUniversalTime() - time.Estimated).Duration() > ClockSkewThreshold
            && !flagsToRaise.Any(f => f.Code == FlagCodes.ClockSkew))
        {
            flagsToRaise.Add((FlagCodes.ClockSkew, "INFO", new Dictionary<string, object?>
            {
                ["command_id"] = command.CommandId.ToString("D"), ["device_time"] = deviceTime.ToUniversalTime().ToString("O"),
                ["estimated_time"] = time.Estimated.ToString("O"),
            }));
        }

        var envelope = CommandEnvelope.For(command.Type, actor, null, command.CommandId, correlationId, command.DeviceTime?.ToUniversalTime(),
            command.BaseVersion, time.Estimated);
        Result<SyncCommandOutcome> outcome;
        try
        {
            outcome = await uow.ExecuteAsync(envelope, async innerCt =>
            {
                var handled = await handler.HandleAsync(envelope, command.Payload, innerCt);
                if (handled.IsSuccess)
                {
                    db.Commands.Add(Receipt(actor, deviceId, command, receipt, SyncResults.Applied, handled.Value.Code, null, handled.Value.EntityId));
                }

                return handled;
            }, ct);
        }
        catch (DbUpdateException)
        {
            // The same command id may have been applied concurrently by another request: then report it as a duplicate.
            var concurrent = await db.Commands.AsNoTracking().SingleOrDefaultAsync(c => c.CommandId == command.CommandId, ct);
            if (concurrent is null)
            {
                throw;
            }

            return await ReplayAsync(concurrent, actor, deviceId, payloadHash, ct);
        }

        if (outcome.IsSuccess)
        {
            return new PushCommandResult(command.CommandId, SyncResults.Applied, outcome.Value.Code, null, outcome.Value.EntityId);
        }

        // The handler's changes were rolled back with its unit of work. Some conflicts are held for a supervisor instead (10 §10.5).
        var error = outcome.Error!;
        if (SyncDispositions.IsQuarantine(error))
        {
            return await QuarantineAsync(actor, deviceId, command, receipt, error.Code, error.Message, correlationId, ct);
        }

        return await RejectAsync(actor, deviceId, command, receipt, error.Code, error.Message, ct);
    }

    private sealed record ReceiptInfo(string Payload, string PayloadHash, Guid? GrantId, TimeEstimate Time);

    /// <summary>Nothing is applied; the command waits for a supervisor (audited, with its receipt, in one transaction).</summary>
    private async Task<PushCommandResult> QuarantineAsync(ActorContext actor, Guid deviceId, PushCommand command, ReceiptInfo receipt, string code,
        string message, string? correlationId, CancellationToken ct)
    {
        var envelope = CommandEnvelope.For(QuarantineCommands.QuarantineCommand, actor, code, command.CommandId, correlationId,
            command.DeviceTime?.ToUniversalTime(), command.BaseVersion, receipt.Time.Estimated);
        await uow.ExecuteAsync<Guid>(envelope, innerCt =>
        {
            var entry = new QuarantinedCommand(Ids.New(), command.CommandId, command.Type, receipt.Payload, actor.UserId!.Value, [.. actor.Roles], deviceId,
                code, message, command.DeviceTime?.ToUniversalTime(), receipt.Time.Estimated, receipt.Time.Confidence, clock.UtcNowMicros(),
                command.GrantId, command.BaseVersion);
            db.Quarantine.Add(entry);
            audit.Record(entry, "sync_quarantine.created", code);
            db.Commands.Add(Receipt(actor, deviceId, command, receipt, SyncResults.Quarantined, code, message, null));
            return Task.FromResult(Result<Guid>.Ok(entry.Id));
        }, ct);
        return new PushCommandResult(command.CommandId, SyncResults.Quarantined, code, message, null);
    }

    private async Task RaiseFlagsAsync(ActorContext actor, Guid deviceId, List<(string Code, string Severity, Dictionary<string, object?> Explanation)> raised,
        CancellationToken ct)
    {
        await uow.ExecuteAsync<bool>(CommandEnvelope.For("RaiseSyncFlags", actor), async innerCt =>
        {
            foreach (var (code, severity, explanation) in raised)
            {
                await flags.RaiseAsync(code, severity, "device", deviceId, explanation, innerCt);
            }

            return true;
        }, ct);
    }

    private async Task<PushCommandResult> ReplayAsync(SyncCommand previous, ActorContext actor, Guid deviceId, string payloadHash, CancellationToken ct)
    {
        if (previous.UserId != actor.UserId || previous.DeviceId != deviceId || previous.PayloadHash != payloadHash)
        {
            return new PushCommandResult(previous.CommandId, SyncResults.Rejected, "COMMAND_ID_CONFLICT", "This command id was already used for a different command.", null);
        }

        // Same command sent again (e.g. the response was lost): report the original outcome.
        if (previous.Result == SyncResults.Quarantined)
        {
            var entry = await db.Quarantine.AsNoTracking().SingleAsync(q => q.CommandId == previous.CommandId, ct);
            return entry.State switch
            {
                QuarantineStates.Applied => new PushCommandResult(previous.CommandId, SyncResults.Duplicate, entry.ResultCode, null, entry.EntityId),
                QuarantineStates.Discarded => new PushCommandResult(previous.CommandId, SyncResults.Rejected, "QUARANTINE_DISCARDED", entry.ResolutionReason, null),
                QuarantineStates.ApplyFailed => new PushCommandResult(previous.CommandId, SyncResults.Rejected, entry.ResultCode, entry.ResolutionReason, null),
                _ => new PushCommandResult(previous.CommandId, SyncResults.Quarantined, previous.ResultCode, previous.ResultMessage, null),
            };
        }

        return previous.Result == SyncResults.Applied
            ? new PushCommandResult(previous.CommandId, SyncResults.Duplicate, previous.ResultCode, null, previous.EntityId)
            : new PushCommandResult(previous.CommandId, previous.Result, previous.ResultCode, previous.ResultMessage, previous.EntityId);
    }

    /// <summary>A rejected command changes nothing, so it has no audit event; its receipt is still recorded.</summary>
    private async Task<PushCommandResult> RejectAsync(
        ActorContext actor, Guid deviceId, PushCommand command, ReceiptInfo receipt, string code, string message, CancellationToken ct)
    {
        db.Commands.Add(Receipt(actor, deviceId, command, receipt, SyncResults.Rejected, code, message, null));
        await db.SaveChangesAsync(ct);
        return new PushCommandResult(command.CommandId, SyncResults.Rejected, code, message, null);
    }

    private SyncCommand Receipt(ActorContext actor, Guid deviceId, PushCommand command, ReceiptInfo receipt,
        string result, string? code, string? message, Guid? entityId) =>
        new(command.CommandId, deviceId, actor.UserId!.Value, command.Type, receipt.Payload, receipt.PayloadHash, command.DeviceTime?.ToUniversalTime(),
            clock.UtcNowMicros(), result, code, message is { Length: > 500 } ? message[..500] : message, entityId,
            receipt.GrantId, receipt.Time.Estimated, receipt.Time.Confidence);

    private async Task<DeviceSyncState> DeviceStateAsync(Guid deviceId, Guid userId, CancellationToken ct)
    {
        var state = await db.DeviceStates.SingleOrDefaultAsync(s => s.DeviceId == deviceId, ct);
        if (state is null)
        {
            state = new DeviceSyncState(deviceId, userId);
            db.DeviceStates.Add(state);
        }

        return state;
    }
}

public sealed class DeviceSyncInfo(SyncDbContext db) : IDeviceSyncInfo
{
    public async Task<IReadOnlyDictionary<Guid, DeviceSyncSnapshot>> ForDevicesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct) =>
        await db.DeviceStates.AsNoTracking()
            .Where(s => deviceIds.Contains(s.DeviceId))
            .ToDictionaryAsync(s => s.DeviceId, s => new DeviceSyncSnapshot(s.LastPullAt, s.LastPushAt, s.LastReportedPendingCommands), ct);
}
