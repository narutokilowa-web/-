using System.Text.Json;
using Mfop.Kernel.Results;

namespace Mfop.Kernel.Commands;

/// <summary>
/// A command that may arrive through the single mobile write path (POST /sync/v1/push).
/// Modules register implementations; the Sync module authorizes <see cref="Permission"/> per command,
/// guarantees idempotency by command id, and records the per-command result.
/// </summary>
public interface ISyncCommandHandler
{
    /// <summary>Command type as sent by the client, e.g. "ReportFault".</summary>
    string Type { get; }

    /// <summary>Permission action (docs/lean-pilot/permissions.yaml) required to run this command.</summary>
    string Permission { get; }

    /// <summary>
    /// Runs the command INSIDE the caller's unit of work (the Sync pipeline opens it, so the idempotency receipt commits
    /// atomically with the change and its audit events). Must not open another unit of work.
    /// </summary>
    Task<Result<SyncCommandOutcome>> HandleAsync(CommandEnvelope command, JsonElement payload, CancellationToken ct);
}

/// <param name="Code">Optional business result code, e.g. CONFIRMED_EXISTING for a de-duplicated fault report.</param>
public sealed record SyncCommandOutcome(Guid EntityId, string? Code = null);

/// <summary>
/// How a handler asks the Sync pipeline to hold a command for a supervisor instead of refusing it (docs/spec/10 §10.5:
/// e.g. work done offline on a work order that was cancelled meanwhile). Nothing the handler did is kept; the command,
/// its reason and its context wait in the quarantine queue.
/// </summary>
public static class SyncDispositions
{
    public const string Key = "disposition", Quarantine = "QUARANTINE";

    public static Error QuarantineFor(string code, string message) =>
        new(code, message, ErrorKind.Conflict, new Dictionary<string, string> { [Key] = Quarantine });

    public static bool IsQuarantine(Error error) => error.Details is { } d && d.TryGetValue(Key, out var v) && v == Quarantine;
}
