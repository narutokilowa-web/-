using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mfop.Access.Api;

/// <summary>
/// Device trust for field-app requests, after <see cref="ActorResolutionMiddleware"/> (user ACTIVE, device ACTIVE and owned):
/// <list type="number">
/// <item>app version below <c>policy.min_app_version</c> → 426 UPGRADE_REQUIRED;</item>
/// <item>trusted-time sample from the phone's monotonic clock (headers), docs/lean-pilot/05 §5.7;</item>
/// <item>on signed endpoints: the device's Keystore signature over method, path, timestamp, nonce and body hash, with a
/// single-use nonce and a bounded timestamp. A stolen token without the phone's key cannot write.</item>
/// </list>
/// </summary>
public sealed class DeviceTrustMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext http, CurrentActor current, AccessDbContext db, PolicyService policies, DeviceClockService clockSamples, TimeProvider clock,
        IOptions<DeviceGuardOptions> options)
    {
        var actor = current.Actor;
        if (actor is null)
        {
            await next(http);
            return;
        }

        var fromFieldApp = http.User.FindFirstValue("azp") == options.Value.MobileClientId;
        var versionHeader = http.Request.Headers[TrustHttp.AppVersionHeader].ToString();
        if (fromFieldApp || !string.IsNullOrEmpty(versionHeader))
        {
            var min = await policies.ValueAsync(PolicyKeys.MinAppVersion, http.RequestAborted);
            AppVersion.TryParse(min, out var minVersion);
            if (!AppVersion.TryParse(versionHeader, out var appVersion) || appVersion < minVersion)
            {
                await Problem(http, StatusCodes.Status426UpgradeRequired, TrustErrorCodes.UpgradeRequired,
                    $"This version of the field app is no longer accepted. Install version {min} or newer.", new() { ["minAppVersion"] = min });
                return;
            }
        }

        var now = clock.GetUtcNow();
        if (actor.DeviceId is { } deviceId
            && long.TryParse(http.Request.Headers[TrustHttp.ElapsedMsHeader], NumberStyles.None, CultureInfo.InvariantCulture, out var elapsed))
        {
            await clockSamples.RecordSampleAsync(deviceId, http.Request.Headers[TrustHttp.BootIdHeader].ToString(), elapsed, now, http.RequestAborted);
        }

        var endpoint = http.GetEndpoint();
        var permission = endpoint?.Metadata.GetMetadata<RequiredPermission>();
        var roleAllowed = permission is null || PermissionMatrix.Allows(permission.Action, actor.Roles); // otherwise the role check answers 403
        if (roleAllowed && endpoint?.Metadata.GetMetadata<DeviceSignatureRequired>() is not null)
        {
            var failure = await VerifySignatureAsync(http, actor, db, now, options.Value);
            if (failure is not null)
            {
                await Problems.Forbidden(failure.Value.Code, failure.Value.Detail).ExecuteAsync(http);
                return;
            }
        }

        await next(http);
    }

    private static async Task<(string Code, string Detail)?> VerifySignatureAsync(
        HttpContext http, ActorContext actor, AccessDbContext db, DateTimeOffset now, DeviceGuardOptions options)
    {
        if (actor.DeviceId is not { } deviceId) return (DeviceErrorCodes.DeviceRequired, "Signed requests come from an approved device.");
        var publicKey = await db.Devices.AsNoTracking().Where(d => d.Id == deviceId).Select(d => d.PublicKey).SingleOrDefaultAsync(http.RequestAborted);
        if (publicKey is null) return (TrustErrorCodes.DeviceKeyRequired, "This device has no signing key. Register the phone again with this app version.");

        var signature = http.Request.Headers[TrustHttp.SignatureHeader].ToString();
        var timestamp = http.Request.Headers[TrustHttp.SignatureTimestampHeader].ToString();
        var nonce = http.Request.Headers[TrustHttp.SignatureNonceHeader].ToString();
        if (signature.Length == 0 || timestamp.Length == 0 || nonce.Length is 0 or > 64)
        {
            return (TrustErrorCodes.SignatureRequired, "This request must be signed by the device.");
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixMs)
            || Math.Abs((now - DateTimeOffset.FromUnixTimeMilliseconds(unixMs)).TotalMinutes) > options.SignatureMaxSkewMinutes)
        {
            return (TrustErrorCodes.SignatureExpired, "The signature time is too far from the server time.");
        }

        http.Request.EnableBuffering();
        string bodyHash;
        using (var sha = SHA256.Create())
        {
            bodyHash = Convert.ToHexStringLower(await sha.ComputeHashAsync(http.Request.Body, http.RequestAborted));
        }

        http.Request.Body.Position = 0;
        var message = TrustHttp.SignedMessage(http.Request.Method, http.Request.PathBase + http.Request.Path, timestamp, nonce, bodyHash);
        if (!DeviceKeys.Verify(publicKey, message, signature))
        {
            return (TrustErrorCodes.SignatureInvalid, "The device signature does not match.");
        }

        db.DeviceNonces.Add(new DeviceNonce(deviceId, nonce, now));
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return (TrustErrorCodes.NonceReused, "This request was already received (replayed nonce).");
        }

        return null;
    }

    private static Task Problem(HttpContext http, int status, string code, string detail, Dictionary<string, string> details) =>
        Results.Problem(statusCode: status, title: code, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code, ["details"] = details }).ExecuteAsync(http);
}
