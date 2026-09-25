using Mfop.Access.Domain;
using Mfop.Assets.Domain;
using Mfop.Faults.Domain;
using Mfop.WorkOrders.Domain;

namespace Mfop.Domain.Tests;

public class M2DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid(), Supervisor = Guid.NewGuid();

    private static Device NewDevice() => new(Guid.NewGuid(), Owner, "ANDROID", "Phone", "14", "0.2.0", Now);

    [Fact]
    public void Device_lifecycle_follows_D1_to_D4()
    {
        var device = NewDevice();
        Assert.Equal(DeviceStates.PendingApproval, device.Status);
        Assert.Equal("SOD_VIOLATION", device.Decide(DeviceCommands.ApproveDevice, Owner, null, Now).Error!.Code);
        Assert.Equal("INVALID_TRANSITION", device.Decide(DeviceCommands.RevokeDevice, Supervisor, "x", Now).Error!.Code);
        Assert.True(device.Decide(DeviceCommands.ApproveDevice, Supervisor, null, Now).IsSuccess);
        Assert.True(device.IsActive);
        Assert.Equal("REASON_REQUIRED", device.Decide(DeviceCommands.RevokeDevice, Supervisor, " ", Now).Error!.Code);
        Assert.True(device.Decide(DeviceCommands.RevokeDevice, Owner, "lost", Now).IsSuccess); // owners may revoke their own device
        Assert.Equal(DeviceStates.Revoked, device.Status);
        Assert.Equal("lost", device.RevokeReason);
        Assert.Equal("INVALID_TRANSITION", device.Decide(DeviceCommands.ApproveDevice, Supervisor, null, Now).Error!.Code);
    }

    [Fact]
    public void Device_system_effects_supersede_and_replace()
    {
        var pending = NewDevice();
        pending.RejectAsSuperseded(Now);
        Assert.Equal((DeviceStates.Rejected, Device.ReasonSuperseded), (pending.Status, pending.DecisionReason));

        var active = NewDevice();
        active.Decide(DeviceCommands.ApproveDevice, Supervisor, null, Now);
        active.RevokeAsReplaced(Supervisor, Now);
        Assert.Equal((DeviceStates.Revoked, Device.ReasonReplaced), (active.Status, active.RevokeReason));
    }

    private static Fault NewFault(string power = "UNKNOWN") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "LIGHT_OFF", "SUPERVISOR", Supervisor, Now, new PowerObservation(power, null, null), "MUNICIPALITY", null, null, "WEB");

    [Fact]
    public void Fault_confirmation_never_changes_state_and_dismissal_is_terminal()
    {
        var fault = NewFault("CONFIRMED_UNAVAILABLE");
        Assert.True(fault.ReportedWithoutPower);
        Assert.Equal("F2", fault.Confirm().Value.Id);
        Assert.Equal(FaultStates.Open, fault.State);
        Assert.True(fault.OnWorkOrderCreated(Guid.NewGuid()).IsSuccess);
        Assert.Equal("F7", fault.Confirm().Value.Id);
        Assert.Equal(FaultStates.InWorkOrder, fault.State);
        Assert.Equal(2, fault.ConfirmationCount);
        Assert.Equal("INVALID_TRANSITION", fault.Dismiss("DUPLICATE", null, Supervisor, Now).Error!.Code);
        Assert.True(fault.OnWorkOrderCancelled().IsSuccess);
        Assert.Null(fault.WorkOrderId);

        Assert.Equal("INVALID_DISMISS_REASON", fault.Dismiss("NOPE", null, Supervisor, Now).Error!.Code);
        Assert.Equal("NOTE_REQUIRED", fault.Dismiss("OTHER", " ", Supervisor, Now).Error!.Code);
        Assert.True(fault.Dismiss("OTHER", "مكرر من بلاغ هاتفي", Supervisor, Now).IsSuccess);
        Assert.Equal(FaultStates.Dismissed, fault.State);
        Assert.False(fault.Confirm().IsSuccess);
        Assert.False(fault.OnWorkOrderCreated(Guid.NewGuid()).IsSuccess);
    }

    [Theory]
    [InlineData("CONFIRMED_AVAILABLE", null, true)]
    [InlineData("UNKNOWN", "RESIDENT", true)]
    [InlineData("MAYBE", null, false)]
    [InlineData("UNKNOWN", "GUESS", false)]
    public void Power_observation_is_a_closed_list(string power, string? source, bool valid) =>
        Assert.Equal(valid, new PowerObservation(power, source, null).Validate() is null);

    [Fact]
    public void Work_order_assignment_and_cancellation_follow_W2_W3_W4()
    {
        var wo = new WorkOrder(Guid.NewGuid(), "WO-2026-000001", Guid.NewGuid(), Guid.NewGuid(), "P2", null, Supervisor, Now, null);
        var techA = Guid.NewGuid();
        Assert.Equal("INVALID_TRANSITION", wo.AssignTo(WorkOrderCommands.Reassign, techA).Error!.Code);
        Assert.Equal("W2", wo.AssignTo(WorkOrderCommands.Assign, techA).Value.Id);
        Assert.Equal("SAME_ASSIGNEE", wo.AssignTo(WorkOrderCommands.Reassign, techA).Error!.Code);
        Assert.Equal("W3", wo.AssignTo(WorkOrderCommands.Reassign, Guid.NewGuid()).Value.Id);
        Assert.Equal("W4", wo.Cancel(Supervisor, "خطأ", Now).Value.Id);
        Assert.True(wo.IsTerminal);
        Assert.Equal("INVALID_TRANSITION", wo.Cancel(Supervisor, "again", Now).Error!.Code);

        var assignment = new WorkOrderAssignment(Guid.NewGuid(), wo.Id, techA, Supervisor, Now);
        assignment.End("OnReassigned", Supervisor, "إجازة", Now);
        Assert.Equal(AssignmentStates.Ended, assignment.State);
    }

    [Fact]
    public void Operational_status_events_only_apply_where_the_machine_allows()
    {
        var light = new StreetLight(Guid.NewGuid(), new StreetLightAttributes(33.5m, 36.2m, PowerSources.Grid, ResponsibleParties.Municipality,
            InstallDatePrecisions.Unknown, OperationalStatuses.Operational), "WEB", Now);
        Assert.Null(light.ApplyStatusEvent("OnFaultReported")); // PENDING asset: no status yet
        light.Activate("SL-000001", Guid.NewGuid(), 1);
        Assert.Equal("OS4", light.ApplyStatusEvent("OnFaultReported")!.Id);
        Assert.Equal(OperationalStatuses.Faulty, light.OperationalStatus);
        Assert.Null(light.ApplyStatusEvent("OnFaultReported")); // already FAULTY: unchanged
    }
}
