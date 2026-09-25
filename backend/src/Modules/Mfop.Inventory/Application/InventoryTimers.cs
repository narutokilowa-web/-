using Mfop.Access.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Inventory.Domain;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Rules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mfop.Inventory.Application;

public sealed class InventoryTimerOptions
{
    public bool RunBackgroundTimers { get; set; } = true;

    public double IntervalSeconds { get; set; } = 60;
}

/// <summary>
/// The timers of 04 §4.9 and §4.11, actor SYSTEM, durations read from the policy settings (never from code):
///   an issue still ISSUED after <c>issue_ack_warning_hours</c> → WARNING ISSUE_UNACKNOWLEDGED (state unchanged);
///   a core return DECLARED_WITH_PART and not received after <c>core_return_overdue_days</c> → WARNING CORE_RETURN_OVERDUE.
/// Each is raised once per document (the time it was raised is kept on the document, audited).
/// </summary>
public sealed class InventoryTimers(InventoryDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IPolicies policies, IFlags flags)
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNowMicros();
        var hours = await policies.IntAsync("issue_ack_warning_hours", ct);
        var days = await policies.IntAsync("core_return_overdue_days", ct);
        var issuedBefore = now.AddHours(-hours);
        var declaredBefore = now.AddDays(-days);
        var issues = await db.Issues.AsNoTracking()
            .Where(i => i.State == StockIssueStates.Issued && i.UnacknowledgedFlaggedAt == null && i.IssuedAt <= issuedBefore)
            .OrderBy(i => i.IssuedAt).Select(i => i.Id).Take(100).ToListAsync(ct);
        var cores = await db.CoreReturns.AsNoTracking()
            .Where(c => c.State == CoreReturnStates.DeclaredWithPart && c.OverdueFlaggedAt == null && c.DeclaredAt <= declaredBefore)
            .OrderBy(c => c.DeclaredAt).Select(c => c.Id).Take(100).ToListAsync(ct);

        foreach (var id in issues)
        {
            await uow.ExecuteAsync<bool>(CommandEnvelope.For("FlagUnacknowledgedIssue", ActorContext.System, "TIMER"), async innerCt =>
            {
                var issue = await db.Issues.SingleAsync(i => i.Id == id, innerCt);
                if (issue.State != StockIssueStates.Issued || issue.UnacknowledgedFlaggedAt is not null) return false;
                issue.MarkUnacknowledgedFlagged(now);
                audit.Record(issue, "stock_issue.unacknowledged_flagged");
                await flags.RaiseAsync(FlagCodes.IssueUnacknowledged, "WARNING", "stock_issue", issue.Id, new Dictionary<string, object?>
                {
                    ["issue_code"] = issue.Code, ["technician_id"] = issue.TechnicianId.ToString("D"), ["issued_at"] = issue.IssuedAt.ToString("O"),
                    ["policy_hours"] = hours,
                }, innerCt);
                return true;
            }, ct);
        }

        foreach (var id in cores)
        {
            await uow.ExecuteAsync<bool>(CommandEnvelope.For("FlagOverdueCoreReturn", ActorContext.System, "TIMER"), async innerCt =>
            {
                var core = await db.CoreReturns.SingleAsync(c => c.Id == id, innerCt);
                if (core.State != CoreReturnStates.DeclaredWithPart || core.OverdueFlaggedAt is not null) return false;
                core.MarkOverdueFlagged(now);
                audit.Record(core, "core_return.overdue_flagged");
                await flags.RaiseAsync(FlagCodes.CoreReturnOverdue, "WARNING", "core_return", core.Id, new Dictionary<string, object?>
                {
                    ["core_return_code"] = core.Code, ["work_order_id"] = core.WorkOrderId.ToString("D"), ["technician_id"] = core.TechnicianId.ToString("D"),
                    ["declared_at"] = core.DeclaredAt?.ToString("O"), ["policy_days"] = days,
                }, innerCt);
                return true;
            }, ct);
        }

        return issues.Count + cores.Count;
    }
}

/// <summary>Runs <see cref="InventoryTimers"/> inside the API process (a separate worker in M8), like the evidence verifier.</summary>
public sealed class InventoryTimerWorker(IServiceScopeFactory scopes, IOptions<InventoryTimerOptions> options, ILogger<InventoryTimerWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunBackgroundTimers)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<InventoryTimers>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Inventory timer pass failed; retrying");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
