using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Mfop.Access;
using Mfop.Access.Application;
using Mfop.Access.Contracts;
using Mfop.Assets;
using Mfop.Assets.Application;
using Mfop.Assets.Contracts;
using Mfop.Assets.Domain;
using Mfop.Audit;
using Mfop.Evidence;
using Mfop.Evidence.Application;
using Mfop.Faults;
using Mfop.Faults.Application;
using Mfop.Faults.Contracts;
using Mfop.Infrastructure;
using Mfop.Inventory;
using Mfop.Inventory.Application;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel.Commands;
using Mfop.Rules;
using Mfop.Sync;
using Mfop.Sync.Application;
using Mfop.Sync.Contracts;
using Mfop.WorkOrders;
using Mfop.WorkOrders.Application;
using Mfop.WorkOrders.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Mfop.Migrator;

/// <summary>
/// DEV ONLY (`make seed`): synthetic DEMO 1 data created through the application's own audited commands, acting as the
/// seeded users (docs/lean-pilot/02 §2.3): items and costs, opening stock, issues to technicians' custody, street lights
/// (registered by registrar1, approved by approver1), and one light with two earlier repairs inside the repeated-failure
/// window, executed by tech1 through the same sync pipeline as the phone (from a dev-only seed device that is revoked at
/// the end), with synthetic in-memory JPEG evidence that goes through the real upload and verification.
/// </summary>
public sealed class DemoSeeder(string ownerConnectionString, IConfiguration configuration, TextWriter log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Phone command payloads use the API's camelCase JSON, exactly as the field app sends them.</summary>
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(string demoFile, CancellationToken ct)
    {
        var data = JsonSerializer.Deserialize<DemoData>(await File.ReadAllTextAsync(demoFile, ct), Json)
                   ?? throw new InvalidOperationException($"Cannot read {demoFile}");
        var users = JsonSerializer.Deserialize<DevUsersFile>(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(demoFile)!, "dev-users.json"), ct), Json)!
            .Users.ToDictionary(u => u.Username, u => new ActorContext(u.Id, u.Roles));

        await using var provider = Services().BuildServiceProvider();
        var marker = data.Items[0].Sku;
        if (await Scoped(provider, sp => sp.GetRequiredService<InventoryDbContext>().Items.AnyAsync(i => i.Sku == marker, ct)))
        {
            log.WriteLine($"demo seed: data already present (item {marker} exists); nothing to do");
            return;
        }

        // Inventory: items with costs (Money = amount + ISO currency + date), opening receipt, one-step issues.
        var itemIds = new Dictionary<string, Guid>();
        foreach (var item in data.Items)
        {
            var id = await Run(provider, sp => sp.GetRequiredService<InventoryService>().DefineItemAsync(Cmd("DefineItem", users["warehouse1"]),
                item.Sku, item.NameAr, item.NameEn, item.Unit, item.SerialTracked, item.CoreReturnRequired, item.Cost.Amount, item.Cost.Currency,
                DateOnly.Parse(item.Cost.AsOf, CultureInfo.InvariantCulture), ct));
            itemIds[item.Sku] = id;
        }

        await Run(provider, sp => sp.GetRequiredService<InventoryService>().RecordReceiptAsync(Cmd("RecordReceipt", users["warehouse1"]),
            new ReceiptRequest(data.OpeningStock.Select(o => new StockLine(itemIds[o.Sku], o.Quantity,
                o.SerialPrefix is null ? null : Enumerable.Range(1, (int)o.Quantity).Select(i => $"{o.SerialPrefix}{i:D4}").ToList())).ToList(),
                "رصيد افتتاحي — بيانات عرض تجريبية"), ct));
        foreach (var issue in data.Issues)
        {
            await Run(provider, sp => sp.GetRequiredService<InventoryService>().IssueAsync(Cmd("IssueStock", users["warehouse1"]),
                new IssueRequest(users[issue.Technician].UserId!.Value,
                    issue.Lines.Select(l => new StockLine(itemIds[l.Sku], l.Serials?.Count ?? l.Quantity ?? 0, l.Serials)).ToList(), "صرف تجريبي"), ct));
        }

        log.WriteLine($"demo seed: {data.Items.Count} items, opening stock, {data.Issues.Count} issues (warehouse1)");

        // Street lights: the repeat-failure light first, then a small synthetic street.
        var repeatLight = await RegisterLightAsync(provider, users, data, 0, data.RepeatFailureLight.Address, "GRID", ct);
        for (var i = 1; i < data.Lights; i++)
        {
            await RegisterLightAsync(provider, users, data, i, $"{data.Street} {i}", data.SolarEvery > 0 && i % data.SolarEvery == 0 ? "SOLAR" : "GRID", ct);
        }

        log.WriteLine($"demo seed: {data.Lights} street lights registered (registrar1) and approved (approver1)");

        // A dev-only seed device for tech1 (approved by admin1), used only for the earlier repairs, revoked at the end.
        var tech = users["tech1"];
        var deviceId = await Run(provider, sp => sp.GetRequiredService<DeviceService>().RegisterAsync(Cmd("RegisterDevice", tech),
            new RegisterDeviceRequest("ANDROID", "DEV-SEED-DEVICE", "0", "seed"), ct));
        await Run(provider, sp => sp.GetRequiredService<DeviceService>().ApproveAsync(Cmd("ApproveDevice", users["admin1"]), deviceId, "DEMO_SEED", ct));
        var techOnDevice = tech with { DeviceId = deviceId };

        var asset = await Scoped(provider, sp => sp.GetRequiredService<AssetQueries>().GetAssetAsync(repeatLight, ct));
        var tagToken = asset.Value.Tag!.HumanCode;
        var n = 0;
        foreach (var repair in data.RepeatFailureLight.EarlierRepairs)
        {
            n++;
            await EarlierRepairAsync(provider, users, techOnDevice, repeatLight, tagToken, itemIds[repair.Sku], repair, n, ct);
        }

        await Run(provider, sp => sp.GetRequiredService<DeviceService>().RevokeAsync(Cmd("RevokeDevice", users["admin1"]), deviceId, "SEED_COMPLETE", ct));
        log.WriteLine($"demo seed: {asset.Value.AssetCode} ({data.RepeatFailureLight.Address}) has {n} earlier repairs closed by supervisor1; " +
                      "the next closed repair of a counted failure produces the repeated-failure recommendation");
    }

    private async Task<Guid> RegisterLightAsync(ServiceProvider provider, Dictionary<string, ActorContext> users, DemoData data, int index, string address,
        string power, CancellationToken ct)
    {
        var attributes = new StreetLightAttributes(
            Math.Round(data.Center.Latitude + 0.0004m * index, 6), Math.Round(data.Center.Longitude + 0.0003m * (index % 3), 6), power, "MUNICIPALITY",
            "YEAR", "OPERATIONAL", address, PoleMaterial: "STEEL", PoleHeightM: 8, LuminaireMake: "Demo", LuminaireWattageW: 50,
            SolarPanelW: power == "GRID" ? null : 100, SolarBatteryAh: power == "GRID" ? null : 100, InstalledOn: new DateOnly(2019, 1, 1));
        var registrationId = await Run(provider, sp => sp.GetRequiredService<RegistrationCommands>().DraftAsync(Cmd("RegisterAssetDraft", users["registrar1"]),
            new RegistrationRequest("BASELINE_SURVEY", null, attributes), ct));
        await Run(provider, sp => sp.GetRequiredService<RegistrationCommands>().SubmitAsync(Cmd("SubmitRegistration", users["registrar1"]), registrationId, ct));
        await Run(provider, sp => sp.GetRequiredService<RegistrationCommands>().ApproveAsync(Cmd("ApproveRegistration", users["approver1"]), registrationId, "بيانات عرض تجريبية", ct));
        var registration = await Scoped(provider, sp => sp.GetRequiredService<AssetQueries>().GetRegistrationAsync(users["approver1"], registrationId, ct));
        return registration.Value.AssetId;
    }

    private async Task EarlierRepairAsync(ServiceProvider provider, Dictionary<string, ActorContext> users, ActorContext tech, Guid assetId, string tagToken,
        Guid itemId, Repair repair, int n, CancellationToken ct)
    {
        var supervisor = users["supervisor1"];
        var fault = await Run(provider, sp => sp.GetRequiredService<FaultService>().ReportAsync(Cmd("ReportFault", supervisor),
            new ReportFaultRequest(assetId, "LIGHT_OFF", "CONFIRMED_AVAILABLE", "OBSERVED_ON_SITE", null, "لا يضيء ليلًا (سجل سابق تجريبي)", "SUPERVISOR"), "WEB", ct));
        var workOrderId = await Run(provider, sp => sp.GetRequiredService<WorkOrderService>().CreateAsync(Cmd("CreateWorkOrder", supervisor),
            new CreateWorkOrderRequest(fault.FaultId, "P2", "إصلاح سابق — بيانات عرض تجريبية"), ct));
        await Run(provider, sp => sp.GetRequiredService<WorkOrderService>().AssignAsync(Cmd("Assign", supervisor), workOrderId,
            new AssignRequest(tech.UserId!.Value, null), ct));

        var templateId = (await Scoped(provider, sp => sp.GetRequiredService<WorkOrderQueries>().TaskForAssigneeAsync(workOrderId, tech, ct))).Value.Progress!.ChecklistTemplateId!.Value;
        var template = (await Scoped(provider, sp => sp.GetRequiredService<WorkOrderReferenceData>().TemplateAsync(templateId, ct)))!;
        var photos = new List<(Guid Id, string Kind, byte[] Bytes)>();
        foreach (var kind in new[] { "BEFORE", "BEFORE", "TEST", "AFTER", "AFTER" })
        {
            photos.Add((Guid.CreateVersion7(), kind, SyntheticPhoto($"{kind} · repair {n}")));
        }

        object Photo(int i) => new
        {
            workOrderId, evidenceId = photos[i].Id, kind = photos[i].Kind, sha256 = Convert.ToHexStringLower(SHA256.HashData(photos[i].Bytes)),
            sizeBytes = photos[i].Bytes.LongLength, mime = "image/jpeg",
        };

        var answers = template.Items.ToDictionary(i => i.Code, i => i.Expected);
        var commands = new List<(string Type, object Payload)>
        {
            ("ArriveOnSite", new { workOrderId, scannedToken = tagToken }),
            ("AddEvidence", Photo(0)),
            ("AddEvidence", Photo(1)),
            ("RecordDiagnosis", new { workOrderId, code = repair.Diagnosis, note = repair.RootCause }),
            ("StartWork", new { workOrderId }),
            ("ConsumeMaterial", new { workOrderId, movementId = Guid.CreateVersion7(), itemId, quantity = 1, serials = new[] { repair.Serial } }),
            ("RecordChecklist", new { workOrderId, checklistId = Guid.CreateVersion7(), templateId, testMethod = repair.TestMethod, answers }),
            ("AddEvidence", Photo(2)),
            ("AddEvidence", Photo(3)),
            ("AddEvidence", Photo(4)),
            ("SubmitWorkOrder", new { workOrderId, rootCauseNote = repair.RootCause }),
        };
        var push = new PushRequest(commands.Select(c => new PushCommand(Guid.CreateVersion7(), c.Type, JsonSerializer.SerializeToElement(c.Payload, PayloadJson), DateTimeOffset.UtcNow)).ToList(), 0);
        var pushed = await Scoped(provider, sp => sp.GetRequiredService<SyncService>().PushAsync(tech, push, "demo-seed", ct));
        var rejected = pushed.Value.Results.FirstOrDefault(r => r.Result == "REJECTED");
        if (rejected is not null) throw new InvalidOperationException($"demo seed push #{pushed.Value.Results.ToList().IndexOf(rejected)}: {rejected.Code} {rejected.Message}");

        foreach (var photo in photos)
        {
            var uploaded = await Scoped(provider, sp => sp.GetRequiredService<EvidenceService>().UploadAsync(tech, photo.Id, new MemoryStream(photo.Bytes), photo.Bytes.LongLength, ct));
            if (!uploaded.IsSuccess) throw new InvalidOperationException($"demo seed upload: {uploaded.Error!.Code}");
        }

        await Scoped(provider, sp => sp.GetRequiredService<EvidenceVerifier>().RunOnceAsync(ct));
        await Run(provider, sp => sp.GetRequiredService<ReviewService>().ApproveAsync(Cmd("ApproveWorkOrder", supervisor), workOrderId,
            new ApproveRequest("إصلاح سابق — بيانات عرض تجريبية"), ct));
    }

    /// <summary>A unique synthetic JPEG (not a photo of anything real).</summary>
    private static byte[] SyntheticPhoto(string label)
    {
        var seed = RandomNumberGenerator.GetBytes(12);
        using var bitmap = new SKBitmap(640, 480);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(seed[0], seed[1], seed[2]));
            using var paint = new SKPaint { Color = new SKColor(seed[3], seed[4], seed[5]), IsAntialias = true };
            for (var i = 0; i < 6; i++)
            {
                canvas.DrawRect(new SKRect(seed[6 + i] * 2, seed[(7 + i) % 12] * 1.5f, seed[6 + i] * 2 + 120, seed[(7 + i) % 12] * 1.5f + 90), paint);
            }

            using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var font = new SKFont { Size = 28 };
            canvas.DrawText("DEMO SEED — " + label, 24, 440, SKTextAlign.Left, font, text);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        return jpeg.ToArray();
    }

    private ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMfopPersistence(ownerConnectionString);
        services.AddAuditModule();
        services.AddAccessModule(configuration);
        services.AddAssetsModule(configuration);
        services.AddFaultsModule();
        services.AddWorkOrdersModule(configuration);
        services.AddRulesModule();
        services.AddInventoryModule();
        services.AddEvidenceModule(configuration, withBackgroundVerifier: false);
        services.AddSyncModule();
        return services;
    }

    private static CommandEnvelope Cmd(string type, ActorContext actor) => CommandEnvelope.For(type, actor, "DEMO_SEED");

    private static async Task<T> Scoped<T>(ServiceProvider provider, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task<T> Run<T>(ServiceProvider provider, Func<IServiceProvider, Task<Mfop.Kernel.Results.Result<T>>> action,
        [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        var result = await Scoped(provider, action);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException($"demo seed (DemoSeeder.cs:{line}): {result.Error!.Code} {result.Error.Message}");
    }

    private sealed record DemoData(Center Center, string Street, int Lights, int SolarEvery, List<ItemData> Items, List<OpeningLine> OpeningStock,
        List<IssueData> Issues, RepeatLight RepeatFailureLight);

    private sealed record Center(decimal Latitude, decimal Longitude);

    private sealed record ItemData(string Sku, string NameAr, string NameEn, string Unit, bool SerialTracked, bool CoreReturnRequired, CostData Cost);

    private sealed record CostData(decimal Amount, string Currency, string AsOf);

    private sealed record OpeningLine(string Sku, decimal Quantity, string? SerialPrefix);

    private sealed record IssueData(string Technician, List<IssueLine> Lines);

    private sealed record IssueLine(string Sku, decimal? Quantity, List<string>? Serials);

    private sealed record RepeatLight(string Address, List<Repair> EarlierRepairs);

    private sealed record Repair(string Diagnosis, string Sku, string Serial, string RootCause, string TestMethod);

    private sealed record DevUsersFile(List<DevUser> Users);

    private sealed record DevUser(Guid Id, string Username, List<string> Roles);
}
