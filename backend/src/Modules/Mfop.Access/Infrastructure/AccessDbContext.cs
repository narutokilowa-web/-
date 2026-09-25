using Mfop.Access.Application;
using Mfop.Access.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Infrastructure;

public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<PolicySetting> PolicySettings => Set<PolicySetting>();
    public DbSet<OfflineGrant> OfflineGrants => Set<OfflineGrant>();
    public DbSet<GrantRevocation> GrantRevocations => Set<GrantRevocation>();
    public DbSet<DeviceNonce> DeviceNonces => Set<DeviceNonce>();
    public DbSet<DeviceClockSample> ClockSamples => Set<DeviceClockSample>();
    public DbSet<IdpTask> IdpTasks => Set<IdpTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AccessModule.Info.Schema);

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.IdpSubject).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.IdpSubject).IsUnique();
            b.Property(x => x.Username).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.Username).IsUnique();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Ignore(x => x.StateName);
            b.Property(x => x.DisableReason).HasMaxLength(500);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasMany(x => x.RoleAssignments).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleAssignment>(b =>
        {
            b.ToTable("role_assignments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Role).HasMaxLength(30).IsRequired();
            b.Property(x => x.GrantReason).HasMaxLength(500).IsRequired();
            b.Property(x => x.RevokeReason).HasMaxLength(500);
            b.HasIndex(x => new { x.UserId, x.Role });
        });

        modelBuilder.Entity<Device>(b =>
        {
            b.ToTable("devices");
            b.HasKey(x => x.Id);
            b.Ignore(x => x.IsActive);
            b.Property(x => x.Platform).HasMaxLength(20).IsRequired();
            b.Property(x => x.Model).HasMaxLength(100).IsRequired();
            b.Property(x => x.OsVersion).HasMaxLength(50).IsRequired();
            b.Property(x => x.AppVersion).HasMaxLength(30).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.DecisionReason).HasMaxLength(500);
            b.Property(x => x.RevokeReason).HasMaxLength(500);
            b.Property(x => x.PublicKey).HasMaxLength(400);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.Status });
        });

        modelBuilder.Entity<PolicySetting>(b =>
        {
            b.ToTable("policy_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(60).IsRequired();
            b.HasIndex(x => x.Key).IsUnique();
            b.Property(x => x.Value).HasMaxLength(100).IsRequired();
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<OfflineGrant>(b =>
        {
            b.ToTable("offline_grants");
            b.HasKey(x => x.Id);
            b.Property(x => x.KeyId).HasMaxLength(60).IsRequired();
            b.Property(x => x.Signature).HasMaxLength(200).IsRequired();
            b.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.DeviceId, x.ExpiresAt });
            b.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<GrantRevocation>(b =>
        {
            b.ToTable("offline_grant_revocations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(40).IsRequired();
            b.HasOne<OfflineGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.GrantId);
        });

        modelBuilder.Entity<DeviceNonce>(b =>
        {
            b.ToTable("device_nonces");
            b.HasKey(x => new { x.DeviceId, x.Nonce });
            b.Property(x => x.Nonce).HasMaxLength(64);
        });

        modelBuilder.Entity<DeviceClockSample>(b =>
        {
            b.ToTable("device_clock_samples");
            b.HasKey(x => new { x.DeviceId, x.BootId });
            b.Property(x => x.BootId).HasMaxLength(DeviceClockService.MaxBootIdLength);
        });

        modelBuilder.Entity<IdpTask>(b =>
        {
            b.ToTable("idp_tasks");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(30).IsRequired();
            b.Property(x => x.IdpSubject).HasMaxLength(200).IsRequired();
            b.Property(x => x.LastError).HasMaxLength(500);
            b.HasIndex(x => new { x.DoneAt, x.NextAttemptAt });
        });
    }
}
