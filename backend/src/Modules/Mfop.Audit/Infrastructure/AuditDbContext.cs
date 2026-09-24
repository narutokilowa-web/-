using Mfop.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Audit.Infrastructure;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> Events => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AuditModule.Info.Schema);
        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("audit_events");
            b.HasKey(x => x.Seq);
            b.Property(x => x.Seq).ValueGeneratedNever();
            b.HasIndex(x => x.EventId).IsUnique();
            b.Property(x => x.Action).HasMaxLength(100);
            b.Property(x => x.EntityType).HasMaxLength(50);
            b.Property(x => x.EntityId).HasMaxLength(100);
            b.HasIndex(x => new { x.EntityType, x.EntityId });
            b.HasIndex(x => x.ActorUserId);
            b.Property(x => x.Changes).HasColumnType("jsonb");
            b.Property(x => x.Facts).HasColumnType("jsonb");
            b.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength();
            b.Property(x => x.PrevHash).HasMaxLength(64).IsFixedLength();
            b.Property(x => x.Hash).HasMaxLength(64).IsFixedLength();
        });
    }
}
