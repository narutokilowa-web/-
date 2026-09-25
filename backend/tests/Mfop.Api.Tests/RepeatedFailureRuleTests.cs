using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mfop.Api.Tests;

/// <summary>The minimal repeated-failure rule reads threshold and window from rule_settings (docs/lean-pilot/11 §11.2 M3 item 10).</summary>
[Collection(nameof(DatabaseCollection))]
public class RepeatedFailureRuleTests(DatabaseFixture db) : IAsyncLifetime
{
    private TestApp App { get; } = new(db);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await App.DisposeAsync();

    private async Task RepairAsync(Guid? assetId, string diagnosis, Action<Guid>? onAsset = null)
    {
        using var flow = await FieldFlow.StartAsync(App, assetId: assetId);
        onAsset?.Invoke(flow.AssetId);
        await flow.WorkUntilReadyToSubmitAsync(diagnosis);
        await flow.ApplyAsync("SubmitWorkOrder", new { workOrderId = flow.WorkOrderId, rootCauseNote = "سبب" });
        await flow.WaitVerifiedAsync();
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        (await supervisor.PostAsJsonAsync($"/api/v1/work-orders/{flow.WorkOrderId}/approve", new { note = (string?)null })).EnsureSuccessStatusCode();
    }

    private async Task<JsonElement[]> RecommendationsAsync(Guid assetId)
    {
        using var supervisor = App.ClientFor(DevUsers.Supervisor);
        return (await supervisor.GetFromJsonAsync<JsonElement>($"/api/v1/recommendations?assetId={assetId}")).EnumerateArray().ToArray();
    }

    private async Task<HttpResponseMessage> ChangeAsync(int threshold, int windowDays, string? reason = "اختبار", string sub = DevUsers.Admin)
    {
        using var client = App.ClientFor(sub);
        return await client.PostAsJsonAsync("/api/v1/settings/rules/repeated-failure", new { threshold, windowDays, enabled = true, reason });
    }

    [Fact]
    public async Task A_changed_threshold_takes_effect_without_a_code_change_and_the_change_is_audited()
    {
        using var admin = App.ClientFor(DevUsers.Admin);
        var before = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/settings/rules")).EnumerateArray().Single(s => s.GetProperty("ruleCode").GetString() == "REPEATED_FAILURE");
        Assert.Equal(3, before.GetProperty("params").GetProperty("threshold").GetInt32());
        Assert.Equal(180, before.GetProperty("params").GetProperty("window_days").GetInt32());

        // Only the admin changes it, and only with a reason.
        Assert.Equal(HttpStatusCode.Forbidden, (await ChangeAsync(2, 180, sub: DevUsers.Supervisor)).StatusCode);
        Assert.Equal("REASON_REQUIRED", await (await ChangeAsync(2, 180, reason: " ")).ProblemCodeAsync());

        try
        {
            (await ChangeAsync(2, 180, reason: "تجربة عتبة أقل")).EnsureSuccessStatusCode();
            Assert.Equal("تجربة عتبة أقل", await db.ScalarAsOwnerAsync<string>(
                "SELECT reason FROM audit.audit_events WHERE action = 'rule_setting.changed' ORDER BY seq DESC LIMIT 1"));

            Guid assetId = default;
            await RepairAsync(null, "LED_MODULE_FAIL", id => assetId = id);
            Assert.Empty(await RecommendationsAsync(assetId)); // threshold − 1

            await RepairAsync(assetId, "DRIVER_FAIL");
            var recommendation = Assert.Single(await RecommendationsAsync(assetId)); // at the (changed) threshold
            Assert.Equal(2, recommendation.GetProperty("threshold").GetInt32());
            Assert.Equal(180, recommendation.GetProperty("windowDays").GetInt32());
            Assert.Equal(2, recommendation.GetProperty("count").GetInt32());
            Assert.Equal(2, recommendation.GetProperty("countedFailures").GetArrayLength());
        }
        finally
        {
            (await ChangeAsync(3, 180, reason: "إعادة القيمة الافتراضية")).EnsureSuccessStatusCode();
        }
    }
}
