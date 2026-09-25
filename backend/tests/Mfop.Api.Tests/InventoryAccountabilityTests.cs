using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mfop.AuditVerify;
using Mfop.Inventory.Application;
using Mfop.Kernel.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Mfop.Api.Tests;

/// <summary>
/// M5 material accountability (docs/lean-pilot/04 §4.9–4.11, 07): issue → acknowledgment → custody → consumption → failed-part
/// return, unused returns, offboarding returns, reconciliation and history. Covers shortages, duplicate movements, invalid returns
/// and reconciliation. Every change goes through a command; the ledger stays balanced and verifiable.
/// </summary>
[Collection(nameof(DatabaseCollection))]
public class InventoryAccountabilityTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly ShiftableClock _clock = new();

    private TestApp App => _app ??= new TestApp(db, clock: _clock);

    private TestApp? _app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    private Task<long> CountAsync(string sql) => db.ScalarAsOwnerAsync<long>(sql);

    private async Task<(Guid Id, string Sku)> ItemAsync(bool serialTracked, bool coreReturn, string unit = "EA")
    {
        var sku = "M5-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using var scope = App.Services.CreateAsyncScope();
        var cmd = CommandEnvelope.For("DefineItem", new ActorContext(Guid.Parse(DevUsers.Warehouse), ["WAREHOUSE"]), "TEST_SETUP");
        var id = await scope.ServiceProvider.GetRequiredService<InventoryService>().DefineItemAsync(cmd, sku, "مادة", "Item", unit, serialTracked, coreReturn,
            1000m, "SYP", new DateOnly(2026, 1, 1), CancellationToken.None);
        return (id.Value, sku);
    }

    private async Task ReceiveAsync(Guid itemId, decimal quantity, string[]? serials = null)
    {
        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        (await warehouse.PostAsJsonAsync("/api/v1/stock/receipts", new { lines = new[] { new { itemId, quantity, serials } }, note = "opening" })).EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> IssueAsync(string technician, params object[] lines)
    {
        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        return await warehouse.PostAsJsonAsync("/api/v1/stock/issues", new { technicianId = technician, lines, note = "صرف" });
    }

    private async Task<Guid> IssuedAsync(string technician, params object[] lines)
    {
        var response = await IssueAsync(technician, lines);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("issueId").GetGuid();
    }

    private async Task<decimal> BalanceAsync(Guid itemId, string kind, string? custodian = null)
    {
        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        var balances = await warehouse.GetFromJsonAsync<JsonElement>("/api/v1/stock/balances");
        return balances.EnumerateArray()
            .Where(b => b.GetProperty("itemId").GetGuid() == itemId && b.GetProperty("locationKind").GetString() == kind
                        && (custodian is null || b.GetProperty("custodianUserId").GetString() == custodian))
            .Sum(b => b.GetProperty("quantity").GetDecimal());
    }

    private async Task<JsonElement> ReconciliationRowAsync(Guid itemId)
    {
        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        var report = await warehouse.GetFromJsonAsync<JsonElement>("/api/v1/stock/reconciliation");
        // Whatever other tests did, the ledger itself is always balanced and in-transit stock always matches open documents.
        Assert.DoesNotContain(report.GetProperty("findings").EnumerateArray(), f =>
            f.GetProperty("code").GetString() is "BALANCE_MISMATCH" or "IN_TRANSIT_MISMATCH" or "CORE_BIN_MISMATCH");
        Assert.DoesNotContain(report.GetProperty("findings").EnumerateArray(), f =>
            f.GetProperty("itemId").ValueKind == JsonValueKind.String && f.GetProperty("itemId").GetGuid() == itemId);
        return report.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("itemId").GetGuid() == itemId);
    }

    private static object Photo(byte[] bytes, Guid id) => new { evidenceId = id, sha256 = TestJpeg.Sha256(bytes), sizeBytes = bytes.LongLength, mime = "image/jpeg" };

    // ---- the whole custody chain (07 §7.3) --------------------------------------------------------------------------------

    [Fact]
    public async Task Full_custody_chain_issue_acknowledge_consume_core_return_unused_return_balances_and_history()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (driver, sku) = await ItemAsync(serialTracked: true, coreReturn: true);
        var serials = Enumerable.Range(1, 10).Select(i => $"{sku}-D{i:D4}").ToArray();
        await ReceiveAsync(driver, 10, serials);
        Assert.Equal(10, await BalanceAsync(driver, "WAREHOUSE"));

        // 2. Issue 2 drivers: they are "in transit", not yet in the technician's custody.
        var issueId = await IssuedAsync(flow.Technician, new { itemId = driver, quantity = 2, serials = serials[..2] });
        Assert.Equal(8, await BalanceAsync(driver, "WAREHOUSE"));
        Assert.Equal(2, await BalanceAsync(driver, "IN_TRANSIT"));
        Assert.Equal(0, await BalanceAsync(driver, "CUSTODY", flow.Technician));
        var pull = await flow.Phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull");
        var pending = pull.GetProperty("stock").GetProperty("issues").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == issueId);
        Assert.Equal("ISSUED", pending.GetProperty("state").GetString());

        // 3. The technician confirms every serial (a missing or unexpected serial is refused: dispute instead).
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = new[] { serials[0] } }), "SERIALS_NOT_CONFIRMED");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = new[] { serials[0], serials[5] } }), "SERIALS_NOT_CONFIRMED");
        await flow.ApplyAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = serials[..2] });
        Assert.Equal(2, await BalanceAsync(driver, "CUSTODY", flow.Technician));
        Assert.Equal(0, await BalanceAsync(driver, "IN_TRANSIT"));
        Assert.Equal(8, await BalanceAsync(driver, "WAREHOUSE"));

        // 4. Consume D1 on the light; the failed driver is declared "with part" with a photo (the light had no known serial).
        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "DRIVER_FAIL" });
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
        var movementId = Guid.CreateVersion7();
        var consumed = await flow.ApplyAsync("ConsumeMaterial", new { workOrderId = flow.WorkOrderId, movementId, itemId = driver, quantity = 1, serials = new[] { serials[0] } });
        Assert.Equal("CORE_RETURN_OPENED", consumed.GetProperty("code").GetString());
        var coreId = await flow.DeclareCoreAsync(movementId, serial: "OLD-DRV-77");

        // 5. The warehouse receives the failed part (serial matches the declared historical serial).
        using (var warehouse = App.ClientFor(DevUsers.Warehouse))
        {
            var received = await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{coreId}/receive", new { outcome = "RECEIVED", serial = "OLD-DRV-77" });
            Assert.Equal(HttpStatusCode.OK, received.StatusCode);
            var core = (await warehouse.GetFromJsonAsync<JsonElement>($"/api/v1/stock/core-returns?technicianId={flow.Technician}")).EnumerateArray()
                .Single(c => c.GetProperty("id").GetGuid() == coreId);
            Assert.Equal("RECEIVED", core.GetProperty("state").GetString());
            Assert.False(core.GetProperty("serialMismatch").GetBoolean());
        }

        // 6. The unused D2 goes back: declared (in transit), then received with the serial counted.
        var returnId = Guid.CreateVersion7();
        await flow.ApplyAsync("DeclareStockReturn", new { returnId, lines = new[] { new { itemId = driver, quantity = 1, serials = new[] { serials[1] } } }, note = "لم تُستخدم" });
        Assert.Equal(0, await BalanceAsync(driver, "CUSTODY", flow.Technician));
        Assert.Equal(1, await BalanceAsync(driver, "IN_TRANSIT"));
        using (var warehouse = App.ClientFor(DevUsers.Warehouse))
        {
            Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/receive", new { confirmedSerials = new[] { serials[1] } })).StatusCode);
        }

        Assert.Equal(0, await BalanceAsync(driver, "CUSTODY", flow.Technician));
        Assert.Equal(9, await BalanceAsync(driver, "WAREHOUSE"));
        Assert.Equal(1, await BalanceAsync(driver, "CONSUMED"));
        Assert.Equal(1, await BalanceAsync(driver, "CORE_BIN"));

        var row = await ReconciliationRowAsync(driver);
        Assert.Equal(10, row.GetProperty("receivedNet").GetDecimal());
        Assert.Equal(9, row.GetProperty("warehouse").GetDecimal());
        Assert.Equal(0, row.GetProperty("inTransit").GetDecimal());
        Assert.Equal(1, row.GetProperty("consumed").GetDecimal());
        Assert.Equal(1, row.GetProperty("coreBin").GetDecimal());
        Assert.Equal(1, row.GetProperty("coresExpected").GetDecimal());

        // 7. History: D1 went warehouse → in transit → custody → consumed and is installed on the light.
        using var auditor = App.ClientFor(DevUsers.Auditor);
        var history = await auditor.GetFromJsonAsync<JsonElement>($"/api/v1/stock/movements?itemId={driver}&serial={serials[0]}");
        Assert.Equal(["RECEIPT", "ISSUE", "ACKNOWLEDGE", "CONSUME"], history.GetProperty("movements").EnumerateArray().Reverse().Select(m => m.GetProperty("type").GetString()!).ToArray());
        Assert.Equal(flow.AssetId, history.GetProperty("serial").GetProperty("installedAssetId").GetGuid());
        Assert.Equal("CONSUMED", history.GetProperty("serial").GetProperty("locationKind").GetString());
        var issueCode = history.GetProperty("movements").EnumerateArray().Single(m => m.GetProperty("type").GetString() == "ISSUE").GetProperty("documentCode").GetString();
        Assert.StartsWith("ISS-", issueCode);

        // The failed part's own record: historical installed serial of the light.
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.serial_units WHERE serial = 'OLD-DRV-77' AND historical AND removed_from_asset_id = '{flow.AssetId}'"));

        // The next repair on this light expects the driver installed now (D1).
        var next = await CountAsync($"SELECT count(*) FROM inventory.serial_units WHERE serial = '{serials[0]}' AND installed_asset_id = '{flow.AssetId}'");
        Assert.Equal(1, next);
    }

    // ---- shortages ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Shortages_are_refused_in_the_warehouse_and_in_custody_and_nothing_moves()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (fuse, _) = await ItemAsync(serialTracked: false, coreReturn: false);
        await ReceiveAsync(fuse, 5);

        var tooMuch = await IssueAsync(flow.Technician, new { itemId = fuse, quantity = 6 });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal("INSUFFICIENT_STOCK", await tooMuch.ProblemCodeAsync());
        Assert.Equal(5, await BalanceAsync(fuse, "WAREHOUSE"));
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM inventory.stock_issues i JOIN inventory.stock_movements m ON m.document_id = i.id WHERE m.item_id = '{fuse}'"));

        // Serial-tracked: a serial that is not in the warehouse (here: already in the technician's custody).
        var notHere = await IssueAsync(flow.Technician, new { itemId = flow.ItemId, quantity = 1, serials = new[] { flow.Serials[0] } });
        Assert.Equal("SERIAL_NOT_IN_WAREHOUSE", await notHere.ProblemCodeAsync());

        var issueId = await IssuedAsync(flow.Technician, new { itemId = fuse, quantity = 3 });
        await flow.ApplyAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = Array.Empty<string>() });

        // More than the custody holds, or a serial the custody does not hold, cannot be declared as returned.
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DeclareStockReturn", new { returnId = Guid.CreateVersion7(), lines = new[] { new { itemId = fuse, quantity = 4 } } }), "INSUFFICIENT_CUSTODY");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DeclareStockReturn", new
        {
            returnId = Guid.CreateVersion7(), lines = new[] { new { itemId = flow.ItemId, quantity = 1, serials = new[] { flow.Serials[0] + "-X" } } },
        }), "SERIAL_NOT_IN_CUSTODY");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DeclareStockReturn", new { returnId = Guid.CreateVersion7(), lines = new[] { new { itemId = fuse, quantity = 0 } } }), "INVALID_QUANTITY");
        Assert.Equal(3, await BalanceAsync(fuse, "CUSTODY", flow.Technician));
        Assert.Equal(2, await BalanceAsync(fuse, "WAREHOUSE"));

        // Issue to someone who is not an active technician.
        var toWarehouse = await IssueAsync(DevUsers.Supervisor, new { itemId = fuse, quantity = 1 });
        Assert.Equal("RECEIVER_NOT_TECHNICIAN", await toWarehouse.ProblemCodeAsync());
        await ReconciliationRowAsync(fuse);
    }

    // ---- duplicate movements ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_commands_and_repeated_steps_never_move_stock_twice()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (cable, _) = await ItemAsync(serialTracked: false, coreReturn: false, unit: "M");
        await ReceiveAsync(cable, 100);
        var issueId = await IssuedAsync(flow.Technician, new { itemId = cable, quantity = 12.5m });

        // The same command sent twice (lost response): one movement, the second reported as DUPLICATE.
        var commandId = Guid.CreateVersion7();
        object Ack() => new { commands = new[] { new { commandId, type = "AcknowledgeStockIssue", payload = new { issueId, confirmedSerials = Array.Empty<string>() }, deviceTime = DateTimeOffset.UtcNow } } };
        var first = await (await flow.Phone.PostAsJsonAsync("/sync/v1/push", Ack())).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await flow.Phone.PostAsJsonAsync("/sync/v1/push", Ack())).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("APPLIED", first.GetProperty("results")[0].GetProperty("result").GetString());
        Assert.Equal("DUPLICATE", second.GetProperty("results")[0].GetProperty("result").GetString());
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.stock_movements WHERE document_id = '{issueId}' AND type = 'ACKNOWLEDGE'"));

        // A second acknowledgment (new command id) is not a transition from ACKNOWLEDGED; nor is cancelling it.
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = Array.Empty<string>() }), "INVALID_TRANSITION");
        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        Assert.Equal("INVALID_TRANSITION", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/issues/{issueId}/cancel", new { reason = "خطأ" })).ProblemCodeAsync());
        Assert.Equal(12.5m, await BalanceAsync(cable, "CUSTODY", flow.Technician));

        // A return id is used once; a return is received once.
        var returnId = Guid.CreateVersion7();
        await flow.ApplyAsync("DeclareStockReturn", new { returnId, lines = new[] { new { itemId = cable, quantity = 2.5m } } });
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DeclareStockReturn", new { returnId, lines = new[] { new { itemId = cable, quantity = 1 } } }), "RETURN_EXISTS");
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/receive", new { confirmedSerials = Array.Empty<string>() })).StatusCode);
        Assert.Equal("INVALID_TRANSITION", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/receive", new { confirmedSerials = Array.Empty<string>() })).ProblemCodeAsync());
        Assert.Equal("INVALID_TRANSITION", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/reject", new { reason = "x" })).ProblemCodeAsync());
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.stock_movements WHERE document_id = '{returnId}' AND type = 'RETURN_RECEIVE'"));

        // The same item twice in one document, or the same serial twice in one line, is refused.
        var twice = await IssueAsync(flow.Technician, new { itemId = cable, quantity = 1 }, new { itemId = cable, quantity = 2 });
        Assert.Equal("DUPLICATE_LINE", await twice.ProblemCodeAsync());
        var sameSerial = await IssueAsync(flow.Technician, new { itemId = flow.ItemId, quantity = 2, serials = new[] { "S-1", "S-1" } });
        Assert.Equal("DUPLICATE_SERIAL", await sameSerial.ProblemCodeAsync());

        // A consumption movement id is used once (offline replay with a new command id).
        var movementId = Guid.CreateVersion7();
        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL" });
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
        await flow.ApplyAsync("ConsumeMaterial", new { workOrderId = flow.WorkOrderId, movementId, itemId = cable, quantity = 3 });
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("ConsumeMaterial", new { workOrderId = flow.WorkOrderId, movementId, itemId = cable, quantity = 3 }), "MOVEMENT_EXISTS");
        Assert.Equal(7, await BalanceAsync(cable, "CUSTODY", flow.Technician));
        await ReconciliationRowAsync(cable);
    }

    // ---- invalid returns ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Invalid_returns_are_refused_and_a_rejected_return_goes_back_to_the_custody_with_a_flag()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 3);
        using var warehouse = App.ClientFor(DevUsers.Warehouse);

        var returnId = Guid.CreateVersion7();
        await flow.ApplyAsync("DeclareStockReturn", new { returnId, lines = new[] { new { itemId = flow.ItemId, quantity = 2, serials = flow.Serials.Take(2).ToArray() } } });
        Assert.Equal(1, await BalanceAsync(flow.ItemId, "CUSTODY", flow.Technician));

        // The counted serials must be exactly the declared ones; a rejection needs a reason; a technician cannot receive.
        var wrong = await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/receive", new { confirmedSerials = new[] { flow.Serials[0] } });
        Assert.Equal("SERIALS_DO_NOT_MATCH", await wrong.ProblemCodeAsync());
        Assert.Equal("REASON_REQUIRED", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/reject", new { reason = " " })).ProblemCodeAsync());
        using (var tech = App.ClientFor(flow.Technician))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/receive", new { confirmedSerials = flow.Serials.Take(2) })).StatusCode);
        }

        // Rejected: REVERSAL back into the technician's custody (the material is still theirs) and a WARNING flag.
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/returns/{returnId}/reject", new { reason = "القطع مكسورة وغير مطابقة" })).StatusCode);
        Assert.Equal(3, await BalanceAsync(flow.ItemId, "CUSTODY", flow.Technician));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'STOCK_RETURN_REJECTED' AND subject_id = '{returnId}' AND severity = 'WARNING'"));
        var returns = await warehouse.GetFromJsonAsync<JsonElement>($"/api/v1/stock/returns?technicianId={flow.Technician}");
        var rejected = returns.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == returnId);
        Assert.Equal("REJECTED", rejected.GetProperty("state").GetString());
        Assert.Contains(rejected.GetProperty("flags").EnumerateArray(), f => f.GetProperty("ruleCode").GetString() == "STOCK_RETURN_REJECTED");
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.stock_movements WHERE document_id = '{returnId}' AND type = 'REVERSAL' AND reversal_of IS NOT NULL"));

        // Nothing to return, or an unknown item.
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DeclareStockReturn", new { returnId = Guid.CreateVersion7(), lines = Array.Empty<object>() }), "LINES_REQUIRED");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DeclareStockReturn", new { returnId = Guid.CreateVersion7(), lines = new[] { new { itemId = Guid.NewGuid(), quantity = 1 } } }), "ITEM_NOT_FOUND");
        await ReconciliationRowAsync(flow.ItemId);
    }

    [Fact]
    public async Task A_disputed_issue_is_flagged_and_cancelled_by_the_warehouse_and_only_the_receiver_may_answer_it()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        using var other = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (photocell, _) = await ItemAsync(serialTracked: false, coreReturn: false);
        await ReceiveAsync(photocell, 4);
        var issueId = await IssuedAsync(flow.Technician, new { itemId = photocell, quantity = 3 });

        await FieldFlow.AssertRejectedAsync(other.PushAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = Array.Empty<string>() }), "NOT_RECEIVER");
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("DisputeStockIssue", new { issueId, reason = "" }), "REASON_REQUIRED");
        var bytes = TestJpeg.Create();
        var photoId = Guid.CreateVersion7();
        await flow.ApplyAsync("DisputeStockIssue", new { issueId, reason = "وصلت قطعتان فقط", photo = Photo(bytes, photoId) });
        Assert.Equal(HttpStatusCode.OK, (await flow.UploadAsync(photoId, bytes)).StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'STOCK_ISSUE_DISPUTED' AND subject_id = '{issueId}' AND severity = 'WARNING'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM evidence.evidence WHERE id = '{photoId}' AND owner_type = 'STOCK_ISSUE' AND owner_id = '{issueId}'"));
        await FieldFlow.AssertRejectedAsync(flow.PushAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = Array.Empty<string>() }), "INVALID_TRANSITION");
        Assert.Equal(3, await BalanceAsync(photocell, "IN_TRANSIT"));

        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        Assert.Equal("REASON_REQUIRED", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/issues/{issueId}/cancel", new { reason = "" })).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/issues/{issueId}/cancel", new { reason = "إعادة الصرف بالكمية الصحيحة" })).StatusCode);
        Assert.Equal(0, await BalanceAsync(photocell, "IN_TRANSIT"));
        Assert.Equal(4, await BalanceAsync(photocell, "WAREHOUSE"));
        var issue = (await warehouse.GetFromJsonAsync<JsonElement>($"/api/v1/stock/issues?technicianId={flow.Technician}")).EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == issueId);
        Assert.Equal("CANCELLED", issue.GetProperty("state").GetString());
        Assert.Equal("وصلت قطعتان فقط", issue.GetProperty("disputeReason").GetString());
        await ReconciliationRowAsync(photocell);
    }

    // ---- core returns -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Core_returns_are_required_before_submission_checked_by_serial_and_waived_only_by_another_supervisor()
    {
        // First repair on a light: the removed part's serial becomes the light's historical installed serial.
        using var first = await FieldFlow.StartAsync(App, custodySerials: 1);
        await first.ArriveAsync();
        await first.PhotoAsync("BEFORE");
        await first.PhotoAsync("BEFORE");
        await first.ApplyAsync("RecordDiagnosis", new { workOrderId = first.WorkOrderId, code = "LED_MODULE_FAIL" });
        await first.ApplyAsync("StartWork", new { workOrderId = first.WorkOrderId });
        var m1 = (await first.ConsumeAsync()).GetProperty("entityId").GetGuid();
        await first.ChecklistAsync();
        await first.PhotoAsync("TEST");
        await first.PhotoAsync("AFTER");
        await first.PhotoAsync("AFTER");
        var refused = await first.SubmitAsync();
        Assert.Equal("CORE_RETURN_NOT_DECLARED", refused.GetProperty("code").GetString()); // W9
        await FieldFlow.AssertRejectedAsync(first.PushAsync("DeclareCoreReturn", new { workOrderId = first.WorkOrderId, movementId = m1, outcome = "WITH_PART" }), "PHOTO_REQUIRED");
        await FieldFlow.AssertRejectedAsync(first.PushAsync("DeclareCoreReturn", new
        {
            workOrderId = first.WorkOrderId, movementId = m1, outcome = "UNRECOVERABLE", photo = Photo(TestJpeg.Create(), Guid.CreateVersion7()),
        }), "REASON_REQUIRED");
        var core1 = await first.DeclareCoreAsync(m1, serial: "HIST-100");
        await FieldFlow.AssertRejectedAsync(first.PushAsync("DeclareCoreReturn", new
        {
            workOrderId = first.WorkOrderId, coreReturnId = core1, outcome = "WITH_PART", photo = Photo(TestJpeg.Create(), Guid.CreateVersion7()),
        }), "INVALID_TRANSITION");
        Assert.Equal("APPLIED", (await first.SubmitAsync()).GetProperty("result").GetString());

        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        var sku = first.Serials[0][..^4];
        // A serial that is in stock cannot be a failed part from a light.
        Assert.Equal("SERIAL_IN_STOCK", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{core1}/receive", new { outcome = "RECEIVED", serial = $"{sku}-W2" })).ProblemCodeAsync());
        Assert.Equal("SERIAL_REQUIRED", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{core1}/receive", new { outcome = "RECEIVED" })).ProblemCodeAsync());

        // Presented serial differs from the reference: the receipt is recorded, and a CRITICAL flag is raised.
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{core1}/receive", new { outcome = "RECEIVED", serial = "HIST-999" })).StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CORE_SERIAL_MISMATCH' AND subject_id = '{core1}' AND severity = 'CRITICAL'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.core_returns WHERE id = '{core1}' AND state = 'RECEIVED' AND serial_mismatch"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.stock_movements WHERE document_id = '{core1}' AND type = 'CORE_RECEIVE'"));

        // The first work order is closed; a second repair on the same light replaces the unit the first repair installed.
        await first.WaitVerifiedAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{first.WorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();
        using var second = await FieldFlow.StartAsync(App, first.Technician, first.AssetId, custodySerials: 1);
        var issue = await IssuedAsync(first.Technician, new { itemId = first.ItemId, quantity = 1, serials = new[] { $"{sku}-W1" } });
        await second.ApplyAsync("AcknowledgeStockIssue", new { issueId = issue, confirmedSerials = new[] { $"{sku}-W1" } });
        await second.ArriveAsync();
        await second.PhotoAsync("BEFORE");
        await second.PhotoAsync("BEFORE");
        await second.ApplyAsync("RecordDiagnosis", new { workOrderId = second.WorkOrderId, code = "LED_MODULE_FAIL" });
        await second.ApplyAsync("StartWork", new { workOrderId = second.WorkOrderId });
        var m2 = Guid.CreateVersion7();
        await second.ApplyAsync("ConsumeMaterial", new { workOrderId = second.WorkOrderId, movementId = m2, itemId = first.ItemId, quantity = 1, serials = new[] { $"{sku}-W1" } });
        var core2 = await second.DeclareCoreAsync(m2, serial: first.Serials[0]);
        var dto = (await warehouse.GetFromJsonAsync<JsonElement>($"/api/v1/stock/core-returns?technicianId={first.Technician}")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == core2);
        Assert.Equal(first.Serials[0], dto.GetProperty("expectedSerial").GetString());
        Assert.Equal($"{sku}-W1", dto.GetProperty("installedSerial").GetString());
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM inventory.serial_units WHERE serial = '{first.Serials[0]}' AND installed_asset_id IS NULL AND removed_from_asset_id = '{first.AssetId}'"));

        // Not presented at the warehouse → DECLARED_UNRECOVERABLE (WARNING); then only a supervisor other than the technician waives it (INFO).
        Assert.Equal("REASON_REQUIRED", await (await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{core2}/receive", new { outcome = "NOT_PRESENTED" })).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{core2}/receive", new { outcome = "NOT_PRESENTED", reason = "لم يحضر الفني القطعة" })).StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CORE_PART_NOT_PRESENTED' AND subject_id = '{core2}'"));
        Assert.Equal(HttpStatusCode.Forbidden, (await warehouse.PostAsJsonAsync($"/api/v1/stock/core-returns/{core2}/waive", new { reason = "x" })).StatusCode);
        Assert.Equal("REASON_REQUIRED", await (await supervisor.PostAsJsonAsync($"/api/v1/stock/core-returns/{core2}/waive", new { reason = "" })).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await supervisor.PostAsJsonAsync($"/api/v1/stock/core-returns/{core2}/waive", new { reason = "القطعة سُرقت من الموقع" })).StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CORE_RETURN_WAIVED' AND subject_id = '{core2}' AND severity = 'INFO'"));
        Assert.Equal("INVALID_TRANSITION", await (await supervisor.PostAsJsonAsync($"/api/v1/stock/core-returns/{core1}/waive", new { reason = "x" })).ProblemCodeAsync());
        await ReconciliationRowAsync(first.ItemId);
    }

    // ---- timers (policy) ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Unacknowledged_issues_and_overdue_core_returns_are_flagged_once_after_the_policy_durations()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (fuse, _) = await ItemAsync(serialTracked: false, coreReturn: false);
        await ReceiveAsync(fuse, 2);
        var issueId = await IssuedAsync(flow.Technician, new { itemId = fuse, quantity = 1 });

        await flow.ArriveAsync();
        await flow.PhotoAsync("BEFORE");
        await flow.PhotoAsync("BEFORE");
        await flow.ApplyAsync("RecordDiagnosis", new { workOrderId = flow.WorkOrderId, code = "LED_MODULE_FAIL" });
        await flow.ApplyAsync("StartWork", new { workOrderId = flow.WorkOrderId });
        var core = await flow.DeclareCoreAsync((await flow.ConsumeAsync()).GetProperty("entityId").GetGuid());

        async Task RunTimersAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<InventoryTimers>().RunOnceAsync(CancellationToken.None);
        }

        await RunTimersAsync();
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM rules.flags WHERE subject_id IN ('{issueId}', '{core}')"));

        _clock.Offset = TimeSpan.FromHours(25); // policy issue_ack_warning_hours = 24
        await RunTimersAsync();
        await RunTimersAsync();
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'ISSUE_UNACKNOWLEDGED' AND subject_id = '{issueId}' AND severity = 'WARNING'"));
        Assert.Equal(0, await CountAsync($"SELECT count(*) FROM rules.flags WHERE subject_id = '{core}'"));
        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        Assert.True((await warehouse.GetFromJsonAsync<JsonElement>($"/api/v1/stock/issues?technicianId={flow.Technician}&state=ISSUED")).EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == issueId).GetProperty("overdue").GetBoolean());

        _clock.Offset = TimeSpan.FromDays(8); // policy core_return_overdue_days = 7
        await RunTimersAsync();
        await RunTimersAsync();
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CORE_RETURN_OVERDUE' AND subject_id = '{core}' AND severity = 'WARNING'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'ISSUE_UNACKNOWLEDGED' AND subject_id = '{issueId}'"));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM audit.audit_events WHERE action = 'stock_issue.unacknowledged_flagged' AND entity_id = '{issueId}'"));
    }

    // ---- offboarding return ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Offboarding_return_is_recorded_by_the_warehouse_only_for_disabled_users_and_what_is_left_is_flagged()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 3);
        var (fuse, _) = await ItemAsync(serialTracked: false, coreReturn: false);
        await ReceiveAsync(fuse, 10);
        var issueId = await IssuedAsync(flow.Technician, new { itemId = fuse, quantity = 5 });
        await flow.ApplyAsync("AcknowledgeStockIssue", new { issueId, confirmedSerials = Array.Empty<string>() });
        var pendingIssue = await IssuedAsync(flow.Technician, new { itemId = fuse, quantity = 1 });

        using var warehouse = App.ClientFor(DevUsers.Warehouse);
        object Return(object[] lines, bool final, string note = "سلّمها مدير القسم") => new { technicianId = flow.Technician, lines, final, note };
        var active = await warehouse.PostAsJsonAsync("/api/v1/stock/offboarding-returns", Return([new { itemId = fuse, quantity = 1 }], false));
        Assert.Equal("USER_STILL_ACTIVE", await active.ProblemCodeAsync());

        using (var admin = App.ClientFor(DevUsers.Admin))
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/users/{flow.Technician}/disable", new { reason = "انتهاء العقد" })).StatusCode);
        }

        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CUSTODY_TO_RECONCILE' AND subject_id = '{flow.Technician}'"));
        var report = await warehouse.GetFromJsonAsync<JsonElement>("/api/v1/stock/reconciliation");
        Assert.Contains(report.GetProperty("findings").EnumerateArray(), f => f.GetProperty("code").GetString() == "CUSTODY_OF_DISABLED_USER"
            && f.GetProperty("userId").GetString() == flow.Technician);

        // No free adjustment: only what is in the custody, with a note.
        Assert.Equal("INSUFFICIENT_CUSTODY", await (await warehouse.PostAsJsonAsync("/api/v1/stock/offboarding-returns", Return([new { itemId = fuse, quantity = 6 }], false))).ProblemCodeAsync());
        Assert.Equal("SERIAL_NOT_IN_CUSTODY", await (await warehouse.PostAsJsonAsync("/api/v1/stock/offboarding-returns",
            Return([new { itemId = flow.ItemId, quantity = 1, serials = new[] { "NOT-HIS" } }], false))).ProblemCodeAsync());
        Assert.Equal("REASON_REQUIRED", await (await warehouse.PostAsJsonAsync("/api/v1/stock/offboarding-returns", Return([new { itemId = fuse, quantity = 1 }], false, " "))).ProblemCodeAsync());

        // Part of the custody comes back.
        var partial = await warehouse.PostAsJsonAsync("/api/v1/stock/offboarding-returns",
            Return([new { itemId = fuse, quantity = 5 }, new { itemId = flow.ItemId, quantity = 2, serials = flow.Serials.Take(2).ToArray() }], false));
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        var body = await partial.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("RET-", body.GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("remaining").GetArrayLength());
        Assert.Equal(0, await BalanceAsync(fuse, "CUSTODY", flow.Technician));
        Assert.Equal(1, await BalanceAsync(flow.ItemId, "CUSTODY", flow.Technician));
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'OFFBOARDING_RETURN_RECORDED' AND subject_id = '{flow.Technician}' AND severity = 'INFO'"));

        // The issue they never acknowledged is cancelled by the warehouse as usual.
        Assert.Equal(HttpStatusCode.OK, (await warehouse.PostAsJsonAsync($"/api/v1/stock/issues/{pendingIssue}/cancel", new { reason = "الموظف غادر" })).StatusCode);

        // Final handover: the last unit was not brought back → it stays in the custody, flagged for review.
        var final = await warehouse.PostAsJsonAsync("/api/v1/stock/offboarding-returns", Return([], true, "لم يُسلَّم الباقي"));
        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        Assert.Equal(1, await CountAsync($"SELECT count(*) FROM rules.flags WHERE rule_code = 'CUSTODY_NOT_RETURNED' AND subject_id = '{flow.Technician}' AND severity = 'WARNING'"));
        Assert.Equal(1, await BalanceAsync(flow.ItemId, "CUSTODY", flow.Technician));
        var returns = await warehouse.GetFromJsonAsync<JsonElement>($"/api/v1/stock/returns?technicianId={flow.Technician}");
        Assert.Contains(returns.EnumerateArray(), r => r.GetProperty("kind").GetString() == "OFFBOARDING" && r.GetProperty("state").GetString() == "RECEIVED"
            && r.GetProperty("lines").GetArrayLength() == 2);
        await ReconciliationRowAsync(fuse);
    }

    // ---- verifier ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_document_state_edited_behind_the_ledger_is_detected_by_the_verifier()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (fuse, _) = await ItemAsync(serialTracked: false, coreReturn: false);
        await ReceiveAsync(fuse, 3);
        var issueId = await IssuedAsync(flow.Technician, new { itemId = fuse, quantity = 2 });
        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
        try
        {
            // A privileged DBA marks the issue acknowledged without the ACKNOWLEDGE movement.
            await db.ExecuteAsOwnerAsync($"UPDATE inventory.stock_issues SET state = 'ACKNOWLEDGED' WHERE id = '{issueId}'");
            var report = await Verifier.RunAsync(db.ReadOnlyConnectionString);
            Assert.Contains(report.Findings, f => f.Code == "IN_TRANSIT_MISMATCH" && f.EntityId == fuse.ToString("D"));
        }
        finally
        {
            await db.ExecuteAsOwnerAsync($"UPDATE inventory.stock_issues SET state = 'ISSUED' WHERE id = '{issueId}'");
        }

        Assert.True((await Verifier.RunAsync(db.ReadOnlyConnectionString)).Ok);
    }

    // ---- reads are authorized ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_technician_sees_only_their_own_documents_in_the_pull_and_cannot_read_warehouse_lists()
    {
        using var flow = await FieldFlow.StartAsync(App, custodySerials: 1);
        using var other = await FieldFlow.StartAsync(App, custodySerials: 1);
        var (fuse, _) = await ItemAsync(serialTracked: false, coreReturn: false);
        await ReceiveAsync(fuse, 2);
        var mine = await IssuedAsync(flow.Technician, new { itemId = fuse, quantity = 1 });
        var theirs = await IssuedAsync(other.Technician, new { itemId = fuse, quantity = 1 });

        var pull = await flow.Phone.GetFromJsonAsync<JsonElement>("/sync/v1/pull");
        var ids = pull.GetProperty("stock").GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(theirs, ids);
        using var tech = App.ClientFor(flow.Technician);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/stock/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/stock/reconciliation")).StatusCode);
    }
}
