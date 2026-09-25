using System.Text.Json;
using Mfop.Inventory.Contracts;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;

namespace Mfop.Inventory.Application;

public sealed record AcknowledgeStockIssuePayload(Guid IssueId, IReadOnlyList<string>? ConfirmedSerials);

public sealed record DisputeStockIssuePayload(Guid IssueId, string? Reason, PhotoAnnouncement? Photo);

public sealed record DeclareStockReturnPayload(Guid ReturnId, IReadOnlyList<StockLine>? Lines, string? Note);

/// <summary>The technician's stock commands on the phone (04 §4.9–4.10), through the single write path /sync/v1/push.</summary>
public abstract class StockCommandHandler<TPayload>(string type, string permission) : ISyncCommandHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => type;

    public string Permission => permission;

    public async Task<Result<SyncCommandOutcome>> HandleAsync(CommandEnvelope command, JsonElement payload, CancellationToken ct)
    {
        TPayload? parsed;
        try
        {
            parsed = payload.Deserialize<TPayload>(Json);
        }
        catch (JsonException)
        {
            parsed = default;
        }

        return parsed is null ? Error.Validation("INVALID_PAYLOAD", $"{type} payload is not valid.") : await RunAsync(command, parsed, ct);
    }

    protected abstract Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope command, TPayload payload, CancellationToken ct);
}

public sealed class AcknowledgeStockIssueHandler(StockDocumentService s)
    : StockCommandHandler<AcknowledgeStockIssuePayload>(Domain.DocumentCommands.AcknowledgeStockIssue, "stock.issue.respond")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, AcknowledgeStockIssuePayload p, CancellationToken ct) =>
        s.AcknowledgeAsync(c, p.IssueId, p.ConfirmedSerials, ct);
}

public sealed class DisputeStockIssueHandler(StockDocumentService s)
    : StockCommandHandler<DisputeStockIssuePayload>(Domain.DocumentCommands.DisputeStockIssue, "stock.issue.respond")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, DisputeStockIssuePayload p, CancellationToken ct) =>
        s.DisputeAsync(c, p.IssueId, p.Reason, p.Photo, ct);
}

public sealed class DeclareStockReturnHandler(StockDocumentService s)
    : StockCommandHandler<DeclareStockReturnPayload>(Domain.DocumentCommands.DeclareStockReturn, "stock.return.declare")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, DeclareStockReturnPayload p, CancellationToken ct) =>
        s.DeclareReturnAsync(c, p.ReturnId, p.Lines, p.Note, ct);
}
