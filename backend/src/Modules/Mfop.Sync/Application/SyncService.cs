using System.Security.Cryptography;
using System.Text;
using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Http;
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
/// The single write path for phones (docs/lean-pilot/01 §1.4): an ordered batch of commands, one result per command.
/// Each command is authorized by its own permission action and runs in its own unit of work together with its receipt,
/// so a command id is applied at most once (idempotency). Conflict resolution, offline grants and device signatures
/// arrive in M4/M6.
/// </summary>
public sealed class SyncService(
    SyncDbContext db,
    IUnitOfWork uow,
    IEnumerable<ISyncCommandHandler> handlers,
    TimeProvider clock,
    IUserDirectory users,
    IWorkOrderTasks tasks,
    IAssetDirectory assets,
    IFaultWorkflow faults)
{
    public const int MaxBatch = 100;

    public async Task<Result<PushResponse>> PushAsync(ActorContext actor, PushRequest request, string? correlationId, CancellationToken ct)
    {
        if (actor.DeviceId is not { } deviceId) return Error.Forbidden(DeviceErrorCodes.DeviceRequired, "Push requires an approved device.");
        var commands = request.Commands ?? [];
        if (commands.Count > MaxBatch) return Error.Validation("BATCH_TOO_LARGE", $"At most {MaxBatch} commands per push.");

        var results = new List<PushCommandResult>(commands.Count);
        foreach (var command in commands)
        {
            results.Add(await ProcessAsync(actor, deviceId, command, correlationId, ct));
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
        return new PullResponse(now, now.ToString("O"), new PullUser(userId, names.GetValueOrDefault(userId, "?"), actor.Roles), deviceId, open);
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
            recent.Select(e => new AssetCardEvent(e.OccurredAt, e.Action, e.ActorName)).ToList());
    }

    private async Task<PushCommandResult> ProcessAsync(ActorContext actor, Guid deviceId, PushCommand command, string? correlationId, CancellationToken ct)
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
            return Replay(previous, actor, deviceId, payloadHash);
        }

        var handler = handlers.FirstOrDefault(h => h.Type == command.Type);
        if (handler is null)
        {
            return await RejectAsync(actor, deviceId, command, payload, payloadHash, "UNKNOWN_COMMAND", $"Unknown command type '{command.Type}'.", ct);
        }

        if (!PermissionMatrix.Allows(handler.Permission, actor.Roles))
        {
            return await RejectAsync(actor, deviceId, command, payload, payloadHash, ErrorCodesHttp.Forbidden, $"Your roles do not allow '{handler.Permission}'.", ct);
        }

        var envelope = CommandEnvelope.For(command.Type, actor, null, command.CommandId, correlationId);
        Result<SyncCommandOutcome> outcome;
        try
        {
            outcome = await uow.ExecuteAsync(envelope, async innerCt =>
            {
                var handled = await handler.HandleAsync(envelope, command.Payload, innerCt);
                if (handled.IsSuccess)
                {
                    db.Commands.Add(Receipt(actor, deviceId, command, payload, payloadHash, SyncResults.Applied, handled.Value.Code, null, handled.Value.EntityId));
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

            return Replay(concurrent, actor, deviceId, payloadHash);
        }

        if (outcome.IsSuccess)
        {
            return new PushCommandResult(command.CommandId, SyncResults.Applied, outcome.Value.Code, null, outcome.Value.EntityId);
        }

        return await RejectAsync(actor, deviceId, command, payload, payloadHash, outcome.Error!.Code, outcome.Error.Message, ct);
    }

    private static PushCommandResult Replay(SyncCommand previous, ActorContext actor, Guid deviceId, string payloadHash)
    {
        if (previous.UserId != actor.UserId || previous.DeviceId != deviceId || previous.PayloadHash != payloadHash)
        {
            return new PushCommandResult(previous.CommandId, SyncResults.Rejected, "COMMAND_ID_CONFLICT", "This command id was already used for a different command.", null);
        }

        // Same command sent again (e.g. the response was lost): report the original outcome.
        return previous.Result == SyncResults.Applied
            ? new PushCommandResult(previous.CommandId, SyncResults.Duplicate, previous.ResultCode, null, previous.EntityId)
            : new PushCommandResult(previous.CommandId, previous.Result, previous.ResultCode, previous.ResultMessage, previous.EntityId);
    }

    /// <summary>A rejected command changes nothing, so it has no audit event; its receipt is still recorded.</summary>
    private async Task<PushCommandResult> RejectAsync(
        ActorContext actor, Guid deviceId, PushCommand command, string payload, string payloadHash, string code, string message, CancellationToken ct)
    {
        db.Commands.Add(Receipt(actor, deviceId, command, payload, payloadHash, SyncResults.Rejected, code, message, null));
        await db.SaveChangesAsync(ct);
        return new PushCommandResult(command.CommandId, SyncResults.Rejected, code, message, null);
    }

    private SyncCommand Receipt(ActorContext actor, Guid deviceId, PushCommand command, string payload, string payloadHash,
        string result, string? code, string? message, Guid? entityId) =>
        new(command.CommandId, deviceId, actor.UserId!.Value, command.Type, payload, payloadHash, command.DeviceTime?.ToUniversalTime(),
            clock.UtcNowMicros(), result, code, message is { Length: > 500 } ? message[..500] : message, entityId);

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
