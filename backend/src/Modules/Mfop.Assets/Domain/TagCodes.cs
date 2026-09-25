using System.Security.Cryptography;
using System.Text;

namespace Mfop.Assets.Domain;

/// <summary>
/// Tag codes: 8 random Crockford base32 characters + a 2-character check code = first 10 bits of
/// HMAC-SHA256(server key, code). The check code rejects codes invented outside the system; it does NOT prevent
/// copying a real QR (nothing can) — presence is proven by several signals, not the QR alone (docs/spec/01 §2.2-ع).
/// </summary>
public static class TagCodes
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(8);
        foreach (var b in bytes)
        {
            sb.Append(Alphabet[b & 31]);
        }

        return sb.ToString();
    }

    public static string CheckCode(string code, string key)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(code));
        var bits = (mac[0] << 8) | mac[1];
        return new string([Alphabet[(bits >> 11) & 31], Alphabet[(bits >> 6) & 31]]);
    }

    /// <summary>Parses "CODE.CHECK" (case-insensitive, spaces/dashes ignored). Returns null when malformed.</summary>
    public static (string Code, string Check)? Parse(string token)
    {
        var cleaned = new string(token.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        var parts = cleaned.Split('.');
        if (parts.Length != 2 || parts[0].Length != 8 || parts[1].Length != 2 || !parts[0].All(Alphabet.Contains) || !parts[1].All(Alphabet.Contains))
        {
            return null;
        }

        return (parts[0], parts[1]);
    }
}

public sealed class TagOptions
{
    /// <summary>Public base URL encoded in QR codes, e.g. https://assets.example.gov/t — resolved by the web app.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5173/t";

    /// <summary>Server secret for check codes (from environment; never committed).</summary>
    public string CheckKey { get; set; } = "";
}
