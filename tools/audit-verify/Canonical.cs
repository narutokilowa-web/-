using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mfop.AuditVerify;

/// <summary>Independent canonical JSON (JCS-style: sorted keys, no whitespace, minimal escaping, integers only).</summary>
public static class Canonical
{
    public static byte[] Bytes(JsonElement e)
    {
        var sb = new StringBuilder();
        Write(e, sb);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static string Sha256Hex(JsonElement e) => Convert.ToHexStringLower(SHA256.HashData(Bytes(e)));

    private static void Write(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var i = 0;
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (i++ > 0) sb.Append(',');
                    Str(p.Name, sb);
                    sb.Append(':');
                    Write(p.Value, sb);
                }

                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var j = 0;
                foreach (var item in e.EnumerateArray())
                {
                    if (j++ > 0) sb.Append(',');
                    Write(item, sb);
                }

                sb.Append(']');
                break;
            case JsonValueKind.String: Str(e.GetString()!, sb); break;
            case JsonValueKind.True: sb.Append("true"); break;
            case JsonValueKind.False: sb.Append("false"); break;
            case JsonValueKind.Null: sb.Append("null"); break;
            case JsonValueKind.Number when e.TryGetInt64(out var l): sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
            default: throw new NotSupportedException($"Unsupported JSON value: {e.GetRawText()}");
        }
    }

    private static void Str(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\b') sb.Append("\\b");
            else if (c == '\f') sb.Append("\\f");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(c);
        }

        sb.Append('"');
    }
}
