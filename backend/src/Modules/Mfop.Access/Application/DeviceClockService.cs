using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

/// <summary>
/// Trusted time (docs/lean-pilot/05 §5.7). Every mobile request that carries the phone's monotonic clock records a sample
/// (server time ↔ elapsed ms on that boot). A command made on the same boot is then placed on the server's clock:
/// <c>sample.server_time + (command.elapsed_ms − sample.elapsed_ms)</c> (HIGH). Otherwise its wall clock is bounded by what
/// the server knows (LOW). The phone's own clock setting never decides anything.
/// </summary>
public sealed class DeviceClockService(AccessDbContext db) : IDeviceClock
{
    public const int MaxBootIdLength = 64;

    public async Task RecordSampleAsync(Guid deviceId, string bootId, long elapsedMs, DateTimeOffset serverTime, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bootId) || bootId.Length > MaxBootIdLength || elapsedMs < 0) return;
        var sample = await db.ClockSamples.SingleOrDefaultAsync(s => s.DeviceId == deviceId && s.BootId == bootId, ct);
        if (sample is null)
        {
            db.ClockSamples.Add(new DeviceClockSample(deviceId, bootId, serverTime, elapsedMs));
        }
        else
        {
            sample.Update(serverTime, elapsedMs);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // two concurrent requests from the same boot: the other one's sample is just as good
            db.ChangeTracker.Clear();
        }
    }

    public async Task<TimeEstimate> EstimateAsync(Guid deviceId, string? bootId, long? elapsedMs, DateTimeOffset? deviceTime, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var lastTrusted = await db.ClockSamples.AsNoTracking().Where(s => s.DeviceId == deviceId).MaxAsync(s => (DateTimeOffset?)s.ServerTime, ct);
        if (!string.IsNullOrWhiteSpace(bootId) && elapsedMs is { } elapsed)
        {
            var sample = await db.ClockSamples.AsNoTracking().SingleOrDefaultAsync(s => s.DeviceId == deviceId && s.BootId == bootId, ct);
            if (sample is not null)
            {
                var estimated = sample.ServerTime.AddMilliseconds(elapsed - sample.ElapsedMs);
                // Never later than the moment the server received it.
                return new TimeEstimate(estimated > receivedAt ? receivedAt : estimated, TimeConfidence.High, lastTrusted);
            }
        }

        var wall = deviceTime ?? receivedAt;
        if (wall > receivedAt) wall = receivedAt;
        return new TimeEstimate(wall, TimeConfidence.Low, lastTrusted);
    }
}

public sealed class DeviceTrust(AccessDbContext db) : IDeviceTrust
{
    public Task<bool> IsActiveAsync(Guid userId, Guid deviceId, CancellationToken ct) =>
        db.Devices.AsNoTracking().AnyAsync(
            d => d.Id == deviceId && d.UserId == userId && d.Status == DeviceStates.Active
                 && db.Users.Any(u => u.Id == userId && u.Status == UserStatus.Active), ct);
}
