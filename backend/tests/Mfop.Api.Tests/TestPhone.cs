using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Access.Contracts;

namespace Mfop.Api.Tests;

/// <summary>
/// What the field app keeps about itself (M4): its device key (the Keystore key on a real phone), its boot session for
/// trusted time, and its current offline grant. Tests change these to play offline, clock and tampering scenarios.
/// </summary>
public sealed class TestPhone : IDisposable
{
    private static readonly ConcurrentDictionary<Guid, TestPhone> ByDevice = new();
    private readonly Stopwatch _sinceBoot = Stopwatch.StartNew();

    public TestPhone() => Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public ECDsa Key { get; private set; }

    public string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    public Guid? DeviceId { get; private set; }

    public string AppVersion { get; set; } = "0.4.0";

    /// <summary>Boot session id; <see cref="Reboot"/> starts a new one (the server then has no clock sample for it).</summary>
    public string BootId { get; private set; } = Guid.NewGuid().ToString("N");

    /// <summary>Added to the monotonic clock, to play time passing while offline.</summary>
    public TimeSpan ElapsedOffset { get; set; }

    public long ElapsedMs => (long)(_sinceBoot.Elapsed + ElapsedOffset).TotalMilliseconds + 60_000;

    /// <summary>When false, requests carry no monotonic-clock headers (an offline phone sends none; the server keeps no sample).</summary>
    public bool SendClockHeaders { get; set; } = true;

    public bool Sign { get; set; } = true;

    /// <summary>
    /// The server-aligned time the app puts in signatures (the real app derives it from the last server time it saw plus its
    /// monotonic clock, so a wrong phone clock does not block sync). Tests that move the server clock move this too.
    /// </summary>
    public Func<DateTimeOffset> SignatureClock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Grant attached to pushed commands that do not name one (fetched on first push when null and <see cref="AutoGrant"/>).</summary>
    public Guid? GrantId { get; set; }

    public bool AutoGrant { get; set; } = true;

    /// <summary>Commands on a work order get the server's current version (as if the phone had just refreshed) unless they carry one.</summary>
    public bool AutoBaseVersion { get; set; } = true;

    public void Reboot()
    {
        BootId = Guid.NewGuid().ToString("N");
        _sinceBoot.Restart();
        ElapsedOffset = TimeSpan.Zero;
    }

    /// <summary>Replaces the key (e.g. an attacker with a copied token but not the Keystore key).</summary>
    public void UseOtherKey()
    {
        Key.Dispose();
        Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    public void Bind(Guid deviceId)
    {
        DeviceId = deviceId;
        ByDevice[deviceId] = this;
    }

    public static TestPhone? For(Guid deviceId) => ByDevice.GetValueOrDefault(deviceId);

    public (string Timestamp, string Nonce, string Signature) SignRequest(string method, string path, byte[] body, DateTimeOffset? at = null, string? nonce = null)
    {
        var timestamp = (at ?? SignatureClock()).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        nonce ??= Guid.NewGuid().ToString("N");
        var message = TrustHttp.SignedMessage(method, path, timestamp, nonce, Convert.ToHexStringLower(SHA256.HashData(body)));
        var signature = Convert.ToBase64String(Key.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        return (timestamp, nonce, signature);
    }

    public void Dispose()
    {
        if (DeviceId is { } id) ByDevice.TryRemove(id, out _);
        Key.Dispose();
    }
}

/// <summary>
/// The field app's HTTP layer in tests: app version and monotonic-clock headers on every request; on push, the current grant
/// and work-order version are filled in (unless the test set them) and the body is signed with the device key.
/// </summary>
public sealed class TestPhoneHandler(TestPhone phone) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Decorate(request);
        var path = request.RequestUri!.AbsolutePath;
        var signedByTest = request.Headers.Contains(TrustHttp.SignatureHeader); // the test built and signed this exact body
        if (request.Method == HttpMethod.Post && path == "/sync/v1/push" && request.Content is not null && !signedByTest)
        {
            var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!;
            foreach (var command in body["commands"]?.AsArray() ?? [])
            {
                if (command is not JsonObject c) continue;
                if (phone.AutoGrant && !c.ContainsKey("grantId"))
                {
                    phone.GrantId ??= await FetchGrantAsync(request, ct);
                    c["grantId"] = phone.GrantId?.ToString("D");
                }

                if (!c.ContainsKey("elapsedMs"))
                {
                    c["elapsedMs"] = phone.ElapsedMs;
                    c["bootId"] = phone.BootId;
                }

                if (phone.AutoBaseVersion && !c.ContainsKey("baseVersion") && c["payload"]?["workOrderId"]?.GetValue<string>() is { } wo)
                {
                    c["baseVersion"] = await WorkOrderVersionAsync(request, Guid.Parse(wo), ct);
                }
            }

            var json = body.ToJsonString();
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (phone.Sign && request.Method != HttpMethod.Get && !signedByTest)
        {
            var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            var (timestamp, nonce, signature) = phone.SignRequest(request.Method.Method, path, bytes);
            request.Headers.Add(TrustHttp.SignatureTimestampHeader, timestamp);
            request.Headers.Add(TrustHttp.SignatureNonceHeader, nonce);
            request.Headers.Add(TrustHttp.SignatureHeader, signature);
        }

        return await base.SendAsync(request, ct);
    }

    private void Decorate(HttpRequestMessage request)
    {
        request.Headers.Remove(TrustHttp.AppVersionHeader);
        request.Headers.Add(TrustHttp.AppVersionHeader, phone.AppVersion);
        if (phone.SendClockHeaders)
        {
            request.Headers.Remove(TrustHttp.ElapsedMsHeader);
            request.Headers.Remove(TrustHttp.BootIdHeader);
            request.Headers.Add(TrustHttp.ElapsedMsHeader, phone.ElapsedMs.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add(TrustHttp.BootIdHeader, phone.BootId);
        }
    }

    private HttpRequestMessage Derived(HttpRequestMessage original, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(original.RequestUri!, path));
        foreach (var header in original.Headers.Where(h => h.Key.StartsWith("X-Test-", StringComparison.Ordinal) || h.Key == "X-Device-Id"))
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        Decorate(request);
        return request;
    }

    private async Task<Guid?> FetchGrantAsync(HttpRequestMessage original, CancellationToken ct)
    {
        using var request = Derived(original, HttpMethod.Post, "/sync/v1/grant");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        var (timestamp, nonce, signature) = phone.SignRequest("POST", "/sync/v1/grant", "{}"u8.ToArray());
        request.Headers.Add(TrustHttp.SignatureTimestampHeader, timestamp);
        request.Headers.Add(TrustHttp.SignatureNonceHeader, nonce);
        request.Headers.Add(TrustHttp.SignatureHeader, signature);
        using var response = await base.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("grantId").GetGuid();
    }

    private async Task<int?> WorkOrderVersionAsync(HttpRequestMessage original, Guid workOrderId, CancellationToken ct)
    {
        using var request = Derived(original, HttpMethod.Get, $"/sync/v1/work-orders/{workOrderId}");
        using var response = await base.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("eventSeq").GetInt32();
    }
}
