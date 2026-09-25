using Mfop.Sync.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Sync.Infrastructure;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<SyncCommand> Commands => Set<SyncCommand>();
    public DbSet<DeviceSyncState> DeviceStates => Set<DeviceSyncState>();
    public DbSet<QuarantinedCommand> Quarantine => Set<QuarantinedCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SyncModule.Info.Schema);

        modelBuilder.Entity<SyncCommand>(b =>
        {
            b.ToTable("sync_commands");
            b.HasKey(x => x.CommandId);
            b.Property(x => x.CommandId).ValueGeneratedNever();
            b.Property(x => x.Type).HasMaxLength(60);
            b.Property(x => x.Payload).HasColumnType("jsonb");
            b.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength();
            b.Property(x => x.Result).HasMaxLength(20);
            b.Property(x => x.ResultCode).HasMaxLength(60);
            b.Property(x => x.ResultMessage).HasMaxLength(500);
            b.HasIndex(x => new { x.DeviceId, x.ReceivedAt });
            b.Property(x => x.TimeConfidence).HasMaxLength(10);
        });

        modelBuilder.Entity<QuarantinedCommand>(b =>
        {
            b.ToTable("quarantine");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.CommandId).IsUnique();
            b.Property(x => x.Type).HasMaxLength(60).IsRequired();
            b.Property(x => x.Payload).HasColumnType("jsonb");
            b.Property(x => x.ReasonCode).HasMaxLength(60).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.Property(x => x.TimeConfidence).HasMaxLength(10).IsRequired();
            b.Property(x => x.State).HasMaxLength(20).IsRequired();
            b.Property(x => x.ResolutionReason).HasMaxLength(500);
            b.Property(x => x.ResultCode).HasMaxLength(60);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.State);
        });

        modelBuilder.Entity<DeviceSyncState>(b =>
        {
            b.ToTable("device_sync_states");
            b.HasKey(x => x.DeviceId);
            b.Property(x => x.DeviceId).ValueGeneratedNever();
        });
    }
}
