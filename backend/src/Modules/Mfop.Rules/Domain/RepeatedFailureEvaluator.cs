namespace Mfop.Rules.Domain;

/// <param name="Threshold">How many counted failures within the window produce a recommendation (default seeded: 3).</param>
/// <param name="WindowDays">Window, in days, back from the evaluation time (default seeded: 180).</param>
public sealed record RepeatedFailureParams(bool Enabled, int Threshold, int WindowDays);

/// <summary>A RESOLVED fault of the asset and the diagnosis of the work order that resolved it.</summary>
public sealed record FailureCandidate(Guid FaultId, Guid WorkOrderId, string WorkOrderCode, string DiagnosisCode, bool CountsAsFailure, DateTimeOffset ResolvedAt);

public sealed record RepeatedFailureOutcome(bool Fires, int Count, IReadOnlyList<FailureCandidate> Counted);

/// <summary>
/// The one transparent rule of DEMO 1: count the asset's RESOLVED faults whose resolving work order has a diagnosis
/// with counts_as_failure = true, resolved within the window. NO_FAULT_FOUND, GRID_SUPPLY_OFF, theft and vandalism
/// (and every other code with counts_as_failure = false) are not counted. No scoring, no prediction.
/// </summary>
public static class RepeatedFailureEvaluator
{
    public static RepeatedFailureOutcome Evaluate(RepeatedFailureParams parameters, IEnumerable<FailureCandidate> candidates, DateTimeOffset now)
    {
        var from = now.AddDays(-parameters.WindowDays);
        var counted = candidates
            .Where(c => c.CountsAsFailure && c.ResolvedAt >= from && c.ResolvedAt <= now)
            .OrderBy(c => c.ResolvedAt)
            .ThenBy(c => c.WorkOrderCode, StringComparer.Ordinal)
            .ToList();
        return new RepeatedFailureOutcome(parameters.Enabled && parameters.Threshold > 0 && counted.Count >= parameters.Threshold, counted.Count, counted);
    }
}
