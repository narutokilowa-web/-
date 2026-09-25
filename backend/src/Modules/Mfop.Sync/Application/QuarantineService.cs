using System.Text.Json;
using Mfop.Access.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Sync.Contracts;
using Mfop.Sync.Domain;
using Mfop.Sync.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Sync.Application;

/// <summary>
/// Supervisor decisions on quarantined commands (docs/lean-pilot/05 §5.5). APPLY runs the command now as the technician who
/// made it (its own audit events are theirs, with their device); the decision itself is attributed to the supervisor.
/// A command of a user or device that is no longer ACTIVE can only be discarded: revoked people do not add new work.
/// </summary>
public sealed class QuarantineService(
    SyncDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IEnumerable<ISyncCommandHandler> handlers, IDeviceTrust trust,
    IUserDirectory users)
{
    public async Task<IReadOnlyList<QuarantineDto>> ListAsync(string? state, CancellationToken ct)
    {
        var query = db.Quarantine.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(q => q.State == state);
        var entries = await query.OrderByDescending(q => q.ReceivedAt).Take(200).ToListAsync(ct);
        var names = await users.DisplayNamesAsync(entries.Select(e => e.UserId).Concat(entries.Where(e => e.ResolvedBy != null).Select(e => e.ResolvedBy!.Value)), ct);
        return entries.Select(e => new QuarantineDto(
            e.Id, e.CommandId, e.Type, e.UserId, names.GetValueOrDefault(e.UserId, "?"), e.DeviceId, e.ReasonCode, e.Reason, e.DeviceTime, e.EstimatedTime,
            e.TimeConfidence, e.ReceivedAt, e.GrantId, null, JsonDocument.Parse(e.Payload).RootElement.Clone(), e.State,
            e.ResolvedBy is { } r ? names.GetValueOrDefault(r) : null, e.ResolvedAt, e.ResolutionReason, e.ResultCode)).ToList();
    }

    public async Task<Result<string>> ResolveAsync(CommandEnvelope cmd, Guid id, ResolveQuarantineRequest request, CancellationToken ct)
    {
        var decision = request.Decision?.Trim().ToUpperInvariant();
        if (decision is not ("APPLY" or "DISCARD")) return Error.Validation("INVALID_DECISION", "Decision must be APPLY or DISCARD.");
        var entry = await db.Quarantine.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, ct);
        if (entry is null) return Error.NotFound("QUARANTINE_NOT_FOUND", "Quarantined command not found.");

        if (decision == "DISCARD")
        {
            return await DecideAsync(cmd, id, QuarantineCommands.DiscardQuarantined, request.Reason, null, null, ct);
        }

        // Guards of Q2 first, so a failure below can only come from the command itself.
        var allowed = SyncStateMachines.Quarantine.Resolve(entry.State, QuarantineCommands.ApplyQuarantined);
        if (!allowed.IsSuccess) return allowed.Error!;
        if (cmd.Actor.UserId == entry.UserId) return Error.Sod("The author of a quarantined command cannot decide on it.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Error.Validation("REASON_REQUIRED", "A reason is required.");
        if (!await trust.IsActiveAsync(entry.UserId, entry.DeviceId, ct))
        {
            return Error.Validation("ACTOR_NOT_ACTIVE", "The technician's account or device is no longer active; this command can only be discarded.");
        }

        var handler = handlers.FirstOrDefault(h => h.Type == entry.Type);
        if (handler is null) return Error.Validation("UNKNOWN_COMMAND", $"Unknown command type '{entry.Type}'.");

        var author = new ActorContext(entry.UserId, entry.UserRoles, entry.DeviceId);
        var envelope = CommandEnvelope.For(entry.Type, author, null, entry.CommandId, cmd.CorrelationId, entry.DeviceTime, entry.BaseVersion, entry.EstimatedTime);
        var payload = JsonDocument.Parse(entry.Payload).RootElement.Clone();
        var applied = await uow.ExecuteAsync<string>(envelope, async innerCt =>
        {
            var handled = await handler.HandleAsync(envelope, payload, innerCt);
            if (!handled.IsSuccess) return handled.Error!;
            var tracked = await db.Quarantine.SingleAsync(q => q.Id == id, innerCt);
            var t = tracked.Resolve(QuarantineCommands.ApplyQuarantined, cmd.Actor.UserId!.Value, request.Reason, clock.UtcNowMicros(),
                handled.Value.Code, handled.Value.EntityId);
            if (!t.IsSuccess) return t.Error!;
            audit.Record(tracked, "sync_quarantine.applied", request.Reason.Trim(), actor: cmd.Actor);
            return tracked.State;
        }, ct);
        if (applied.IsSuccess) return applied;

        // The command no longer fits the current state (e.g. the work order moved on): record that the supervisor tried.
        var failed = await DecideAsync(cmd, id, QuarantineCommands.RecordApplyFailure, $"{request.Reason.Trim()} — {applied.Error!.Code}", applied.Error.Code, null, ct);
        return failed.IsSuccess ? Error.Conflict(applied.Error.Code, $"The command could not be applied: {applied.Error.Message}") : failed;
    }

    private Task<Result<string>> DecideAsync(CommandEnvelope cmd, Guid id, string command, string? reason, string? resultCode, Guid? entityId, CancellationToken ct) =>
        uow.ExecuteAsync<string>(cmd, async innerCt =>
        {
            var entry = await db.Quarantine.SingleAsync(q => q.Id == id, innerCt);
            var t = entry.Resolve(command, cmd.Actor.UserId!.Value, reason, clock.UtcNowMicros(), resultCode, entityId);
            if (!t.IsSuccess) return t.Error!;
            audit.Record(entry, command == QuarantineCommands.DiscardQuarantined ? "sync_quarantine.discarded" : "sync_quarantine.apply_failed", reason!.Trim());
            return entry.State;
        }, ct);
}
