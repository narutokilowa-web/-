using Mfop.Access.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Infrastructure;

public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<Device> Devices => Set<Device>();

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
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.UserId, x.Status });
        });
    }
}
