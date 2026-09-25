namespace Mfop.WorkOrders.Application;

/// <summary>Bound from configuration section "WorkOrders". Municipality policy: never hard-coded.</summary>
public sealed class WorkOrderOptions
{
    /// <summary>
    /// SLA per priority in hours (e.g. {"P1": 4}). Empty by default: the municipality has not defined SLAs yet,
    /// so work orders are created without a due time until this is configured.
    /// </summary>
    public Dictionary<string, int> SlaHoursByPriority { get; set; } = [];

    /// <summary>IANA time zone of the organization; used for the year in work-order codes (WO-2026-000123).</summary>
    public string TimeZone { get; set; } = "Asia/Damascus";

    /// <summary>W1 effect: a new work order is marked as rework of one closed as REPAIRED on the same light within this many days (04 §4.8, configurable).</summary>
    public int ReworkWindowDays { get; set; } = 30;
}
