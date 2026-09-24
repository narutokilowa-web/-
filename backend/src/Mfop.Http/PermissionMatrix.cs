namespace Mfop.Http;

/// <summary>
/// Action → allowed roles. Mirrors docs/lean-pilot/permissions.yaml EXACTLY (a test compares them).
/// Record-level policies (maker ≠ checker, assignee only, …) are enforced in the command handlers.
/// </summary>
public static class PermissionMatrix
{
    private const string T = "TECHNICIAN", I = "INSPECTOR", S = "SUPERVISOR", R = "REGISTRAR", A = "APPROVER", W = "WAREHOUSE", U = "AUDITOR", D = "ADMIN";

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Actions = new Dictionary<string, IReadOnlySet<string>>
    {
        ["me.read"] = Set(T, I, S, R, A, W, U, D),
        ["asset.read"] = Set(T, I, S, R, A, U),
        ["asset.history.read"] = Set(T, I, S, R, A, U),
        ["tag.resolve"] = Set(T, I, S, R, A, U),
        ["registration.draft"] = Set(R),
        ["registration.submit"] = Set(R),
        ["registration.discard"] = Set(R),
        ["registration.decide"] = Set(A),
        ["registration.read"] = Set(R, A, U),
        ["audit.read"] = Set(U),
        ["device.register"] = Set(T, I, S, R, A, W, U),
        ["device.approve"] = Set(D, S),
        ["device.revoke"] = Set(D, S),
        ["fault.report"] = Set(S),
        ["fault.dismiss"] = Set(S),
        ["workorder.create"] = Set(S),
        ["workorder.assign"] = Set(S),
        ["workorder.cancel"] = Set(S),
        ["workorder.read_assigned"] = Set(T),
        ["workorder.read"] = Set(S, U),
        ["workorder.execute"] = Set(T),
        ["material.consume"] = Set(T),
        ["workorder.review"] = Set(S),
        ["evidence.preview"] = Set(S, A, U, T),
        ["evidence.original"] = Set(U),
        ["stock.receipt"] = Set(W),
        ["stock.issue"] = Set(W),
        ["stock.balances.read"] = Set(W, S, U),
        ["stock.custody.read_own"] = Set(T),
        ["dashboard.read"] = Set(S, U),
        ["recommendation.read"] = Set(T, I, S, R, A, U),
        ["policy.settings.change"] = Set(D),
        ["user.manage"] = Set(D),
    };

    public static bool Allows(string action, IEnumerable<string> roles) =>
        Actions.TryGetValue(action, out var allowed)
            ? roles.Any(allowed.Contains)
            : throw new ArgumentException($"Unknown action '{action}'.", nameof(action));

    private static HashSet<string> Set(params string[] roles) => new(roles, StringComparer.Ordinal);
}
