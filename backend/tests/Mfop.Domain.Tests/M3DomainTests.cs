using Mfop.Evidence.Domain;
using Mfop.Inventory.Domain;
using Mfop.Rules.Domain;
using Mfop.WorkOrders.Domain;

namespace Mfop.Domain.Tests;

public class M3DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly RepeatedFailureParams Default = new(Enabled: true, Threshold: 3, WindowDays: 180);

    private static FailureCandidate Failure(int daysAgo, string code = "LED_MODULE_FAIL", bool counts = true) =>
        new(Guid.NewGuid(), Guid.NewGuid(), $"WO-2026-{daysAgo:D6}", code, counts, Now.AddDays(-daysAgo));

    [Fact]
    public void Repeated_failure_does_not_fire_one_below_the_threshold()
    {
        var outcome = RepeatedFailureEvaluator.Evaluate(Default, [Failure(10), Failure(40)], Now);
        Assert.False(outcome.Fires);
        Assert.Equal(2, outcome.Count);
    }

    [Fact]
    public void Repeated_failure_fires_at_the_threshold_and_lists_the_counted_work_orders_oldest_first()
    {
        var outcome = RepeatedFailureEvaluator.Evaluate(Default, [Failure(1), Failure(90), Failure(179)], Now);
        Assert.True(outcome.Fires);
        Assert.Equal(3, outcome.Count);
        Assert.Equal(["WO-2026-000179", "WO-2026-000090", "WO-2026-000001"], outcome.Counted.Select(c => c.WorkOrderCode));
    }

    [Fact]
    public void Failures_outside_the_window_are_not_counted()
    {
        var outcome = RepeatedFailureEvaluator.Evaluate(Default, [Failure(1), Failure(90), Failure(181)], Now);
        Assert.False(outcome.Fires);
        Assert.Equal(2, outcome.Count);
    }

    [Theory]
    [InlineData("NO_FAULT_FOUND")]
    [InlineData("GRID_SUPPLY_OFF")]
    [InlineData("THEFT")]
    [InlineData("VANDALISM")]
    public void Diagnoses_that_do_not_count_as_failure_are_excluded(string code)
    {
        var outcome = RepeatedFailureEvaluator.Evaluate(Default, [Failure(1), Failure(2), Failure(3, code, counts: false)], Now);
        Assert.False(outcome.Fires);
        Assert.DoesNotContain(outcome.Counted, c => c.DiagnosisCode == code);
    }

    [Fact]
    public void A_changed_threshold_or_window_or_a_disabled_rule_changes_the_outcome()
    {
        FailureCandidate[] candidates = [Failure(1), Failure(200)];
        Assert.True(RepeatedFailureEvaluator.Evaluate(Default with { Threshold = 2, WindowDays = 365 }, candidates, Now).Fires);
        Assert.False(RepeatedFailureEvaluator.Evaluate(Default with { Threshold = 2 }, candidates, Now).Fires);
        Assert.False(RepeatedFailureEvaluator.Evaluate(Default with { Enabled = false, Threshold = 1 }, candidates, Now).Fires);
    }

    private static EvidenceRecord NewEvidence() =>
        new(Guid.NewGuid(), "WORK_ORDER", Guid.NewGuid(), EvidenceKinds.Before, new string('a', 64), 1000, "image/jpeg", Guid.NewGuid(), Guid.NewGuid(), Now, Now);

    [Fact]
    public void Evidence_follows_E1_to_E3_and_cannot_be_verified_before_upload()
    {
        var evidence = NewEvidence();
        Assert.Equal(EvidenceStates.Announced, evidence.State);
        Assert.Equal("INVALID_TRANSITION", evidence.MarkVerified("k", null, null, Now).Error!.Code);
        Assert.True(evidence.MarkUploaded(new string('a', 64), 1000, Now).IsSuccess);
        Assert.Equal(EvidenceStates.Uploaded, evidence.State);
        Assert.True(evidence.MarkVerified("k", "p", "t", Now).IsSuccess);
        Assert.Equal(EvidenceStates.Verified, evidence.State);
        Assert.Equal("INVALID_TRANSITION", evidence.MarkUploaded(new string('a', 64), 1000, Now).Error!.Code);
    }

    [Fact]
    public void Evidence_hash_mismatch_E4_and_duplicate_E5_are_terminal()
    {
        var mismatch = NewEvidence();
        mismatch.MarkUploaded(new string('b', 64), 1000, Now);
        Assert.True(mismatch.MarkHashMismatch(Now).IsSuccess);
        Assert.Equal(EvidenceStates.HashMismatch, mismatch.State);
        Assert.False(mismatch.MarkVerified("k", null, null, Now).IsSuccess);

        var duplicate = NewEvidence();
        duplicate.MarkUploaded(new string('a', 64), 1000, Now);
        var original = Guid.NewGuid();
        Assert.True(duplicate.MarkDuplicate(original, Now).IsSuccess);
        Assert.Equal(EvidenceStates.Duplicate, duplicate.State);
        Assert.Equal(original, duplicate.DuplicateOf);
    }

    [Fact]
    public void Stock_ledger_moves_supplier_to_warehouse_to_transit_to_custody_to_consumed_only()
    {
        var ledger = InventoryStateMachines.StockLedger;
        Assert.Equal(LocationKinds.Warehouse, ledger.Resolve(LocationKinds.Supplier, LedgerCommands.RecordReceipt).Value.To);
        Assert.Equal(LocationKinds.InTransit, ledger.Resolve(LocationKinds.Warehouse, LedgerCommands.IssueStock).Value.To); // M5: two-party handover
        Assert.Equal(LocationKinds.Custody, ledger.Resolve(LocationKinds.InTransit, LedgerCommands.AcknowledgeStockIssue).Value.To);
        Assert.Equal(LocationKinds.Consumed, ledger.Resolve(LocationKinds.Custody, LedgerCommands.ConsumeMaterial).Value.To);
        Assert.False(ledger.Resolve(LocationKinds.Warehouse, LedgerCommands.ConsumeMaterial).IsSuccess);
        Assert.False(ledger.Resolve(LocationKinds.Consumed, LedgerCommands.CancelStockIssue).IsSuccess);
        Assert.Equal(LocationKinds.Warehouse, ledger.Resolve(LocationKinds.InTransit, LedgerCommands.CancelStockIssue).Value.To);
    }

    [Theory]
    [InlineData("CLOSED")]
    [InlineData("CANCELLED")]
    [InlineData("SUBMITTED")]
    [InlineData("ASSIGNED")]
    public void No_field_command_is_allowed_outside_the_execution_states(string state)
    {
        foreach (var command in WorkOrderStateMachines.InStateCommands.Select(c => c.Command))
        {
            Assert.Equal("INVALID_TRANSITION", WorkOrderStateMachines.AllowedInState(state, command).Error!.Code);
        }
    }

    [Fact]
    public void Before_photos_only_on_site_and_materials_only_after_start_work()
    {
        Assert.True(WorkOrderStateMachines.AllowedInState(WorkOrderStates.OnSite, WorkOrderCommands.AddEvidence).IsSuccess);
        Assert.False(WorkOrderStateMachines.AllowedInState(WorkOrderStates.OnSite, WorkOrderCommands.ConsumeMaterial).IsSuccess);
        Assert.True(WorkOrderStateMachines.AllowedInState(WorkOrderStates.InProgress, WorkOrderCommands.ConsumeMaterial).IsSuccess);
        Assert.False(WorkOrderStateMachines.AllowedInState(WorkOrderStates.Returned, WorkOrderCommands.RecordDiagnosis).IsSuccess);
    }

    [Fact]
    public void Seal_formats_are_fixed_width_and_culture_independent()
    {
        Assert.Equal("2026-09-24T12:00:00.000000Z", WorkOrderSeal.Time(Now.ToOffset(TimeSpan.FromHours(3))));
        Assert.Equal("1.000", WorkOrderSeal.Quantity(1m));
        Assert.Equal("2.500", WorkOrderSeal.Quantity(2.5m));
    }
}
