using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Mfop.Inventory.Application;
using Mfop.Kernel.Commands;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Mfop.Api.Tests;

/// <summary>A synthetic JPEG with unique content (the verifier decodes it to make previews).</summary>
public static class TestJpeg
{
    public static byte[] Create(int width = 96, int height = 64)
    {
        var seed = RandomNumberGenerator.GetBytes(8);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(seed[0], seed[1], seed[2]));
            using var paint = new SKPaint { Color = new SKColor(seed[3], seed[4], seed[5]) };
            canvas.DrawRect(new SKRect(seed[6] % 40, seed[7] % 30, 60, 50), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <summary>Drives one work order through the phone's single write path, like the field app does.</summary>
public sealed class FieldFlow : IDisposable
{
    private readonly TestApp _app;
    private readonly HttpClient _phone;

    private FieldFlow(TestApp app, string technician, Guid deviceId, Guid assetId, Guid faultId, Guid workOrderId, string tagToken)
    {
        _app = app;
        Technician = technician;
        DeviceId = deviceId;
        AssetId = assetId;
        FaultId = faultId;
        WorkOrderId = workOrderId;
        TagToken = tagToken;
        _phone = app.FieldAppClientFor(technician, deviceId);
    }

    public string Technician { get; }
    public Guid DeviceId { get; }
    public Guid AssetId { get; }
    public Guid FaultId { get; }
    public Guid WorkOrderId { get; }
    public string TagToken { get; }
    public Guid ItemId { get; private set; }
    public List<string> Serials { get; } = [];
    public HttpClient Phone => _phone;

    /// <summary>
    /// A new ACTIVE light with a fault and a work order assigned to <paramref name="technician"/> (a fresh technician by default,
    /// with an approved phone), plus a serial-tracked item issued to their custody by warehouse1.
    /// </summary>
    public static async Task<FieldFlow> StartAsync(TestApp app, string? technician = null, Guid? assetId = null, int custodySerials = 2)
    {
        technician ??= await app.CreateUserAsync("TECHNICIAN");
        var deviceId = await app.ActiveDeviceAsync(technician);
        Guid faultId;
        if (assetId is { } existing)
        {
            using var sup = app.ClientFor(DevUsers.Supervisor);
            var r = await sup.PostAsJsonAsync("/api/v1/faults", ApiHelpers.SampleFault(existing));
            r.EnsureSuccessStatusCode();
            faultId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("faultId").GetGuid();
        }
        else
        {
            (assetId, faultId) = await app.ActiveAssetWithFaultAsync();
        }

        var workOrderId = await app.CreateWorkOrderAsync(faultId);
        using var supervisor = app.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{workOrderId}/assign", new { assigneeId = technician, reason = (string?)null })).EnsureSuccessStatusCode();
        var asset = await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/assets/{assetId}");
        var flow = new FieldFlow(app, technician, deviceId, assetId!.Value, faultId, workOrderId, asset.GetProperty("tag").GetProperty("humanCode").GetString()!);
        await flow.StockAsync(custodySerials);
        return flow;
    }

    /// <summary>A unique serial-tracked item (cost in SYP), received and issued to this technician by warehouse1 through the API.</summary>
    public async Task StockAsync(int serials)
    {
        var sku = "T-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var cmd = CommandEnvelope.For("DefineItem", new ActorContext(Guid.Parse(DevUsers.Warehouse), ["WAREHOUSE"]), "TEST_SETUP");
            var id = await scope.ServiceProvider.GetRequiredService<InventoryService>().DefineItemAsync(cmd, sku, "مادة اختبار", "Test item", "EA", true, true,
                185000m, "SYP", new DateOnly(2026, 1, 1), CancellationToken.None);
            ItemId = id.Value;
        }

        Serials.AddRange(Enumerable.Range(1, serials).Select(i => $"{sku}-{i:D3}"));
        using var warehouse = _app.ClientFor(DevUsers.Warehouse);
        (await warehouse.PostAsJsonAsync("/api/v1/stock/receipts", new { lines = new[] { new { itemId = ItemId, quantity = serials + 2, serials = Serials.Concat([$"{sku}-W1", $"{sku}-W2"]).ToArray() } } })).EnsureSuccessStatusCode();
        (await warehouse.PostAsJsonAsync("/api/v1/stock/issues", new { technicianId = Technician, lines = new[] { new { itemId = ItemId, quantity = serials, serials = Serials.ToArray() } } })).EnsureSuccessStatusCode();
    }

    public async Task<JsonElement> PushAsync(string type, object payload)
    {
        var response = await _phone.PostAsJsonAsync("/sync/v1/push", new
        {
            commands = new[] { new { commandId = Guid.CreateVersion7(), type, payload, deviceTime = DateTimeOffset.UtcNow } },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")[0];
    }

    public async Task<JsonElement> ApplyAsync(string type, object payload)
    {
        var result = await PushAsync(type, payload);
        Assert.True(result.GetProperty("result").GetString() == "APPLIED",
            $"{type}: {result.GetProperty("result").GetString()} {result.GetProperty("code")} {result.GetProperty("message")}");
        return result;
    }

    public static async Task AssertRejectedAsync(Task<JsonElement> push, string code)
    {
        var result = await push;
        Assert.Equal("REJECTED", result.GetProperty("result").GetString());
        Assert.Equal(code, result.GetProperty("code").GetString());
    }

    public Task<JsonElement> ArriveAsync() => ApplyAsync("ArriveOnSite", new { workOrderId = WorkOrderId, scannedToken = TagToken });

    /// <summary>Announces a photo (AddEvidence) and uploads its bytes. Returns the evidence id.</summary>
    public async Task<Guid> PhotoAsync(string kind, byte[]? bytes = null, bool upload = true, string? announcedHash = null)
    {
        bytes ??= TestJpeg.Create();
        var id = Guid.CreateVersion7();
        await ApplyAsync("AddEvidence", new { workOrderId = WorkOrderId, evidenceId = id, kind, sha256 = announcedHash ?? TestJpeg.Sha256(bytes), sizeBytes = bytes.LongLength, mime = "image/jpeg" });
        if (upload)
        {
            var response = await UploadAsync(id, bytes);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        return id;
    }

    public Task<HttpResponseMessage> UploadAsync(Guid evidenceId, byte[] bytes, HttpClient? client = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return (client ?? _phone).PutAsync($"/sync/v1/evidence/{evidenceId}/content", content);
    }

    public async Task<Guid> TemplateIdAsync()
    {
        var task = await _phone.GetFromJsonAsync<JsonElement>($"/sync/v1/work-orders/{WorkOrderId}");
        return task.GetProperty("progress").GetProperty("checklistTemplateId").GetGuid();
    }

    public async Task<JsonElement> TemplateAsync()
    {
        var id = await TemplateIdAsync();
        var pull = await _phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull");
        return pull.GetProperty("reference").GetProperty("checklistTemplates").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
    }

    public async Task ChecklistAsync(string testMethod = "NIGHT", Dictionary<string, string>? overrides = null, string? notTestedReason = null)
    {
        var template = await TemplateAsync();
        var answers = template.GetProperty("items").EnumerateArray()
            .Where(i => testMethod != "NOT_TESTED" || !i.GetProperty("testItem").GetBoolean())
            .ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("expected").GetString()!);
        foreach (var (k, v) in overrides ?? []) answers[k] = v;
        await ApplyAsync("RecordChecklist", new { workOrderId = WorkOrderId, checklistId = Guid.CreateVersion7(), templateId = template.GetProperty("id").GetGuid(), testMethod, notTestedReason, answers });
    }

    public Task<JsonElement> ConsumeAsync(int quantity = 1, string[]? serials = null) =>
        ApplyAsync("ConsumeMaterial", new { workOrderId = WorkOrderId, movementId = Guid.CreateVersion7(), itemId = ItemId, quantity, serials = serials ?? Serials.Take(quantity).ToArray() });

    /// <summary>Arrive → 2 before photos → diagnosis → start → 1 material → checklist → test photo → 2 after photos.</summary>
    public async Task WorkUntilReadyToSubmitAsync(string diagnosis = "LED_MODULE_FAIL")
    {
        await ArriveAsync();
        await PhotoAsync("BEFORE");
        await PhotoAsync("BEFORE");
        await ApplyAsync("RecordDiagnosis", new { workOrderId = WorkOrderId, code = diagnosis, note = "وحدة الليد لا تعمل" });
        await ApplyAsync("StartWork", new { workOrderId = WorkOrderId });
        await ConsumeAsync();
        await ChecklistAsync();
        await PhotoAsync("TEST");
        await PhotoAsync("AFTER");
        await PhotoAsync("AFTER");
    }

    public Task<JsonElement> SubmitAsync(string type = "SubmitWorkOrder", string? noMaterialReason = null) =>
        PushAsync(type, new { workOrderId = WorkOrderId, rootCauseNote = "احتراق وحدة الليد", noMaterialReason });

    public async Task<JsonElement> ReviewAsync(string? as_ = null)
    {
        using var supervisor = _app.ClientFor(as_ ?? DevUsers.Supervisor);
        return await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/work-orders/{WorkOrderId}/review");
    }

    /// <summary>Waits until the in-process verifier has processed every uploaded photo of the work order.</summary>
    public async Task<JsonElement> WaitVerifiedAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            var review = await ReviewAsync();
            if (review.GetProperty("pendingEvidence").GetInt32() == 0) return review;
            await Task.Delay(200);
        }

        throw new TimeoutException("evidence was not verified in time");
    }

    public void Dispose() => _phone.Dispose();
}
