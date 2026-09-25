using System.Globalization;
using Mfop.Access.Contracts;
using Mfop.Inventory.Contracts;
using Mfop.Rules.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Application;

/// <summary>
/// docs/lean-pilot/05 §5.6 steps 7–8, inside the DisableUser unit of work: each open work order of the user is flagged
/// NEEDS_REASSIGNMENT (WARNING) for the supervisor's queue, and a custody balance that is not zero raises CUSTODY_TO_RECONCILE
/// (INFO; returning the materials is a warehouse/HR task). Nothing is reassigned or moved automatically.
/// </summary>
public sealed class WorkOrderOffboarding(WorkOrdersDbContext db, IInventoryLedger inventory, IFlags flags) : IUserOffboardingHandler
{
    public async Task OnUserDisabledAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var open = await db.WorkOrders.AsNoTracking()
            .Where(w => w.AssigneeId == userId && w.State != WorkOrderStates.Closed && w.State != WorkOrderStates.Cancelled)
            .OrderBy(w => w.CreatedAt).ToListAsync(ct);
        foreach (var w in open)
        {
            await flags.RaiseAsync(FlagCodes.NeedsReassignment, "WARNING", "work_order", w.Id, new Dictionary<string, object?>
            {
                ["assignee_id"] = userId.ToString("D"), ["state"] = w.State, ["reason"] = "USER_DISABLED",
            }, ct);
        }

        var custody = (await inventory.CustodyOfAsync(userId, ct)).Where(c => c.Quantity != 0).ToList();
        if (custody.Count > 0)
        {
            await flags.RaiseAsync(FlagCodes.CustodyToReconcile, "INFO", "user", userId, new Dictionary<string, object?>
            {
                ["items"] = string.Join(",", custody.Select(c => $"{c.Sku}:{c.Quantity.ToString(CultureInfo.InvariantCulture)}")),
            }, ct);
        }
    }
}
