using System.Data;
using System.Text.Json.Nodes;
using Mfop.Infrastructure.Auditing;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Mfop.Infrastructure.Persistence;

public interface IUnitOfWork
{
    /// <summary>
    /// Runs a command handler in one database transaction. On success, every changed entity must have a staged
    /// audit record; old/new values are captured from the change tracker; entity changes and audit events are
    /// committed together. On failure (or exception) everything is rolled back.
    /// </summary>
    Task<Result<T>> ExecuteAsync<T>(CommandEnvelope command, Func<CancellationToken, Task<Result<T>>> handler, CancellationToken ct);
}

public sealed class AuditMissingException(string message) : InvalidOperationException(message);

public sealed class UnitOfWork(
    DbSession session,
    IEnumerable<IUnitOfWorkParticipant> participants,
    AuditTrail trail,
    IAuditAppender appender,
    TimeProvider clock) : IUnitOfWork
{
    private readonly IReadOnlyList<DbContext> _contexts = participants.Select(p => p.Context).ToList();

    public async Task<Result<T>> ExecuteAsync<T>(CommandEnvelope command, Func<CancellationToken, Task<Result<T>>> handler, CancellationToken ct)
    {
        if (session.Transaction is not null)
        {
            throw new InvalidOperationException("Nested units of work are not supported.");
        }

        if (session.Connection.State != ConnectionState.Open)
        {
            await session.Connection.OpenAsync(ct);
        }

        await using var tx = await session.Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        session.Transaction = tx;
        trail.Clear();
        try
        {
            foreach (var context in _contexts)
            {
                await context.Database.UseTransactionAsync(tx, ct);
            }

            var result = await handler(ct);
            if (!result.IsSuccess)
            {
                await RollbackAsync(tx);
                return result;
            }

            var events = BuildAuditEvents(command);
            foreach (var context in _contexts)
            {
                await context.SaveChangesAsync(ct);
            }

            await appender.AppendAsync(session, events, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(tx);
            return Error.Conflict(ErrorCodes.ConcurrencyConflict, "The record was changed by someone else. Reload and try again.");
        }
        catch
        {
            await RollbackAsync(tx);
            throw;
        }
        finally
        {
            foreach (var context in _contexts)
            {
                await context.Database.UseTransactionAsync(null, CancellationToken.None);
            }

            session.Transaction = null;
            trail.Clear();
        }
    }

    private async Task RollbackAsync(Npgsql.NpgsqlTransaction tx)
    {
        await tx.RollbackAsync(CancellationToken.None);
        foreach (var context in _contexts)
        {
            context.ChangeTracker.Clear();
        }
    }

    private List<AuditEventDraft> BuildAuditEvents(CommandEnvelope command)
    {
        var changed = new List<EntityEntry>();
        foreach (var context in _contexts)
        {
            context.ChangeTracker.DetectChanges();
            changed.AddRange(context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
        }

        var changesByEntity = new Dictionary<(string Type, Guid Id), (JsonObject Changes, int? Version)>();
        foreach (var entry in changed)
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException($"DELETE_FORBIDDEN: {entry.Metadata.ClrType.Name} rows are never deleted.");
            }

            if (entry.Entity is not IAuditable auditable)
            {
                throw new AuditMissingException($"{entry.Metadata.ClrType.Name} is persisted but does not implement IAuditable.");
            }

            BumpVersion(entry);
            changesByEntity[(auditable.AuditEntityType, auditable.Id)] = (CaptureChanges(entry), ReadVersion(entry));
        }

        var staged = trail.Records;
        foreach (var key in changesByEntity.Keys)
        {
            if (!staged.Any(r => r.Entity.AuditEntityType == key.Type && r.Entity.Id == key.Id))
            {
                throw new AuditMissingException($"AUDIT_MISSING: {key.Type} {key.Id} changed without an audit record.");
            }
        }

        var occurredAt = clock.UtcNowMicros();
        var attached = new HashSet<(string, Guid)>();
        var events = new List<AuditEventDraft>();
        foreach (var record in staged)
        {
            var key = (record.Entity.AuditEntityType, record.Entity.Id);
            var changes = new JsonObject();
            int? version = null;
            if (changesByEntity.TryGetValue(key, out var captured))
            {
                version = captured.Version;
                if (attached.Add(key))
                {
                    changes = captured.Changes; // old/new values go with the first record for that entity
                }
            }

            JsonObject? facts = null;
            if (record.Extra is { Count: > 0 })
            {
                facts = [];
                foreach (var (name, value) in record.Extra)
                {
                    facts[name] = AuditValues.ToNode(value);
                }
            }

            events.Add(new AuditEventDraft(
                Ids.New(), occurredAt, command.Actor.UserId, command.Actor.Roles, command.Actor.DeviceId,
                record.Action, record.Entity.AuditEntityType, record.Entity.Id.ToString("D"), version,
                changes, facts, record.Reason ?? command.Reason, command.CommandId, command.CorrelationId));
        }

        return events;
    }

    private static void BumpVersion(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var version = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Version" && p.Metadata.ClrType == typeof(int));
        if (version is not null && !version.IsModified)
        {
            version.CurrentValue = (int)version.OriginalValue! + 1;
        }
    }

    private static int? ReadVersion(EntityEntry entry) =>
        entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Version" && p.Metadata.ClrType == typeof(int))?.CurrentValue as int?;

    private static JsonObject CaptureChanges(EntityEntry entry)
    {
        var changes = new JsonObject();
        foreach (var property in entry.Properties)
        {
            var column = property.Metadata.GetColumnName();
            if (entry.State == EntityState.Added)
            {
                if (property.CurrentValue is not null)
                {
                    changes[column] = new JsonObject { ["old"] = null, ["new"] = AuditValues.ToNode(property.CurrentValue) };
                }
            }
            else if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
            {
                changes[column] = new JsonObject
                {
                    ["old"] = AuditValues.ToNode(property.OriginalValue),
                    ["new"] = AuditValues.ToNode(property.CurrentValue),
                };
            }
        }

        return changes;
    }
}
