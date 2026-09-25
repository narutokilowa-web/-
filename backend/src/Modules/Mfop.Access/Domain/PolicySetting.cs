using System.Globalization;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;

namespace Mfop.Access.Domain;

/// <summary>A policy key with its default and the rule a value must satisfy (docs/lean-pilot/05 §5.9).</summary>
public sealed record PolicyDefinition(string Key, string Default, string Description, Func<string, string?> Validate);

/// <summary>
/// The security policies of M4 and the inventory timers of M5. Values are data in <c>access.policy_settings</c> (seeded with these defaults by the migrator,
/// changed by the admin, audited); nothing reads a duration from code.
/// </summary>
public static class PolicyKeys
{
    public const string OfflineGraceHours = "offline_grace_hours";
    public const string GrantWindowToleranceMinutes = "grant_window_tolerance_minutes";
    public const string AppLockMinutes = "app_lock_minutes";
    public const string MinAppVersion = "min_app_version";

    // M5 (docs/lean-pilot/04 §4.9, §4.11): the timers of the inventory flows.
    public const string IssueAckWarningHours = "issue_ack_warning_hours";
    public const string CoreReturnOverdueDays = "core_return_overdue_days";

    public static readonly IReadOnlyList<PolicyDefinition> All =
    [
        new(OfflineGraceHours, "24", "Offline grant lifetime (hours)", v => IntInRange(v, 1, 72)),
        new(GrantWindowToleranceMinutes, "10", "Tolerance of the server grant-window check (minutes)", v => IntInRange(v, 0, 60)),
        new(AppLockMinutes, "5", "App PIN lock after this long in the background (minutes)", v => IntInRange(v, 1, 30)),
        new(MinAppVersion, "0.5.0", "Oldest field-app version the API accepts (older → 426 update required)", v => AppVersion.TryParse(v, out _) ? null : "must look like 1.2.3"),
        new(IssueAckWarningHours, "24", "Stock issue not acknowledged after this long → WARNING flag ISSUE_UNACKNOWLEDGED (hours)", v => IntInRange(v, 1, 168)),
        new(CoreReturnOverdueDays, "7", "Failed part declared but not received after this long → WARNING flag CORE_RETURN_OVERDUE (days)", v => IntInRange(v, 1, 90)),
    ];

    public static PolicyDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    private static string? IntInRange(string value, int min, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max ? null : $"must be a whole number from {min} to {max}";
}

/// <summary>Numeric app version (major.minor.patch; anything after '+' or '-' is ignored).</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch) : IComparable<AppVersion>
{
    public static bool TryParse(string? value, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var core = value.Trim().Split('+', '-')[0];
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public int CompareTo(AppVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed class PolicySetting : IAuditable
{
    private PolicySetting() { }

    public PolicySetting(Guid id, string key, string value, Guid? changedBy, DateTimeOffset changedAt)
    {
        Id = id;
        Key = key;
        Value = value;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }

    public string AuditEntityType => "policy_setting";

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public int Version { get; private set; }

    public Result<bool> Change(string value, Guid changedBy, DateTimeOffset now)
    {
        var definition = PolicyKeys.Find(Key)!;
        var invalid = definition.Validate(value.Trim());
        if (invalid is not null) return Error.Validation("INVALID_POLICY_VALUE", $"{Key} {invalid}.");
        Value = value.Trim();
        ChangedBy = changedBy;
        ChangedAt = now;
        return true;
    }
}
