using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mfop.Access.Application;

/// <summary>Configuration section "Grants". The private key is a secret (environment / secret store), never in the repository.</summary>
public sealed class GrantSigningOptions
{
    /// <summary>EC P-256 private key, PKCS#8 DER, base64 (make env generates one per installation).</summary>
    public string? SigningKey { get; set; }

    /// <summary>Key id sent with every grant, so the key can be rotated (docs/lean-pilot/05 §5.5).</summary>
    public string KeyId { get; set; } = "grant-key-1";
}

/// <summary>
/// Signs offline grants with the server's grant key (ECDSA P-256 / SHA-256, DER). Deviation from 05 §5.5 (Ed25519):
/// P-256 is built into .NET and Android without extra libraries (ADR-0008).
/// </summary>
public sealed class GrantSigner : IDisposable
{
    private readonly ECDsa _key;

    public GrantSigner(IOptions<GrantSigningOptions> options, ILogger<GrantSigner> log)
    {
        KeyId = options.Value.KeyId;
        _key = ECDsa.Create();
        if (string.IsNullOrWhiteSpace(options.Value.SigningKey))
        {
            // Development convenience only: grants issued before a restart then fail the phone's check and are simply renewed online.
            log.LogWarning("Grants:SigningKey is not configured: using an ephemeral grant key (set it in production).");
            _key.GenerateKey(ECCurve.NamedCurves.nistP256);
            KeyId = "ephemeral-" + Convert.ToHexStringLower(SHA256.HashData(_key.ExportSubjectPublicKeyInfo()))[..12];
        }
        else
        {
            Import(_key, Convert.FromBase64String(options.Value.SigningKey.Trim()));
        }

        PublicKey = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    }

    public string KeyId { get; }

    /// <summary>X.509 SubjectPublicKeyInfo, base64.</summary>
    public string PublicKey { get; }

    public string Sign(byte[] payload) =>
        Convert.ToBase64String(_key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    public void Dispose() => _key.Dispose();

    /// <summary>PKCS#8 (documented format) or SEC1 `ECPrivateKey` (what `openssl genpkey -outform DER` writes); P-256 only.</summary>
    private static void Import(ECDsa key, byte[] der)
    {
        try
        {
            key.ImportPkcs8PrivateKey(der, out _);
        }
        catch (CryptographicException)
        {
            key.ImportECPrivateKey(der, out _);
        }

        if (key.KeySize != 256) throw new CryptographicException("Grants:SigningKey must be an EC P-256 key.");
    }
}
