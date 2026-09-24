using Mfop.Access.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Infrastructure;

public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

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
    }
}
