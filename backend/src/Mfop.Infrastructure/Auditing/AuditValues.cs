using System.Globalization;
using System.Text.Json.Nodes;

namespace Mfop.Infrastructure.Auditing;

/// <summary>
/// Renders CLR values for audit old/new values. Only strings, booleans, integers and null reach the hashed JSON
/// (decimals, dates, ids, enums become strings) so canonical hashing is unambiguous.
/// </summary>
public static class AuditValues
{
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create((long)i),
        long l => JsonValue.Create(l),
        short sh => JsonValue.Create((long)sh),
        decimal d => JsonValue.Create(FormatDecimal(d)),
        double db => JsonValue.Create(db.ToString("R", CultureInfo.InvariantCulture)),
        float f => JsonValue.Create(f.ToString("R", CultureInfo.InvariantCulture)),
        Guid g => JsonValue.Create(g.ToString("D")),
        DateTimeOffset dto => JsonValue.Create(FormatTimestamp(dto)),
        DateTime dt => JsonValue.Create(FormatTimestamp(new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)))),
        DateOnly d => JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        Enum e => JsonValue.Create(e.ToString()),
        string[] arr => new JsonArray([.. arr.Select(x => (JsonNode?)JsonValue.Create(x))]),
        _ => throw new NotSupportedException($"Audit value type {value.GetType().Name} is not supported."),
    };

    /// <summary>Decimal without trailing zeros, invariant culture (e.g. 33.5138, 10).</summary>
    public static string FormatDecimal(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>UTC, microsecond precision, e.g. 2026-09-24T13:05:00.123456Z (PostgreSQL timestamptz precision).</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}
