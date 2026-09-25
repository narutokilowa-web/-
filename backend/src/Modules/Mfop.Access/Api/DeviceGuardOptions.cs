namespace Mfop.Access.Api;

/// <summary>Bound from configuration section "Auth".</summary>
public sealed class DeviceGuardOptions
{
    /// <summary>
    /// Keycloak client id of the field app. Tokens issued to this client (claim "azp") are only accepted together with
    /// an ACTIVE device (header X-Device-Id), so a revoked phone cannot fall back to the web API.
    /// </summary>
    public string MobileClientId { get; set; } = "mfop-mobile";

    /// <summary>
    /// How far a device signature's timestamp may be from the server clock (the phone signs with its trusted-time estimate).
    /// Replays inside the window are stopped by single-use nonces.
    /// </summary>
    public double SignatureMaxSkewMinutes { get; set; } = 15;
}
