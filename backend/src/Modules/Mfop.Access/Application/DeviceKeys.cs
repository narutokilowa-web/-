using System.Security.Cryptography;
using System.Text;

namespace Mfop.Access.Application;

/// <summary>Device public keys and request signatures (ECDSA P-256 / SHA-256, DER). docs/lean-pilot/05 §5.4.</summary>
public static class DeviceKeys
{
    public static bool IsValidPublicKey(string? spkiBase64)
    {
        if (string.IsNullOrWhiteSpace(spkiBase64) || spkiBase64.Length > 400) return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64.Trim()), out _);
            return key.KeySize == 256;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static bool Verify(string spkiBase64, string message, string signatureBase64)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64), out _);
            return key.VerifyData(Encoding.UTF8.GetBytes(message), Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
