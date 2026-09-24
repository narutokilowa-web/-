using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mfop.Kernel.Json;

/// <summary>
/// RFC 8785 (JCS)-style canonical JSON for hashing: object keys sorted by UTF-16 code units, no insignificant
/// whitespace, minimal string escaping (non-ASCII such as Arabic is written as-is in UTF-8).
/// Restriction (by design): numbers must be integers. Decimals, dates and ids are recorded as strings.
/// tools/audit-verify contains an independent implementation; a test asserts both produce identical bytes.
/// </summary>
public static class CanonicalJson
{
    public static byte[] Serialize(JsonNode? node) =>
        Serialize(node is null ? JsonDocument.Parse("null").RootElement : JsonSerializer.SerializeToElement(node));

    public static byte[] Serialize(JsonElement element)
    {
        var sb = new StringBuilder();
        Write(element, sb);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] Sha256(JsonNode? node) => SHA256.HashData(Serialize(node));

    public static string Sha256Hex(JsonNode? node) => Convert.ToHexStringLower(Sha256(node));

    public static string Sha256Hex(JsonElement element) => Convert.ToHexStringLower(SHA256.HashData(Serialize(element)));

    private static void Write(JsonElement element, StringBuilder sb)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    WriteString(property.Name, sb);
                    sb.Append(':');
                    Write(property.Value, sb);
                }

                sb.Append('}');
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        sb.Append(',');
                    }

                    firstItem = false;
                    Write(item, sb);
                }

                sb.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, sb);
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            case JsonValueKind.Null:
                sb.Append("null");
                break;
            case JsonValueKind.Number when element.TryGetInt64(out var l):
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new NotSupportedException($"Canonical JSON supports only objects, arrays, strings, booleans, integers and null (got {element.ValueKind} '{element.GetRawText()}').");
        }
    }

    private static void WriteString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }
}
