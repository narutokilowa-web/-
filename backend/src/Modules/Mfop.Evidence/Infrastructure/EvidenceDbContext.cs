using Mfop.Evidence.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Evidence.Infrastructure;

public sealed class EvidenceDbContext(DbContextOptions<EvidenceDbContext> options) : DbContext(options)
{
    public DbSet<EvidenceRecord> Evidence => Set<EvidenceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(EvidenceModule.Info.Schema);

        modelBuilder.Entity<EvidenceRecord>(b =>
        {
            b.ToTable("evidence");
            b.HasKey(x => x.Id);
            b.Ignore(x => x.IsSuperseded);
            b.Property(x => x.OwnerType).HasMaxLength(30);
            b.Property(x => x.Kind).HasMaxLength(20);
            b.Property(x => x.Sha256Device).HasColumnName("sha256_device").HasMaxLength(64).IsFixedLength();
            b.Property(x => x.Sha256Server).HasColumnName("sha256_server").HasMaxLength(64).IsFixedLength();
            b.Property(x => x.Mime).HasMaxLength(40);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.StorageKey).HasMaxLength(200);
            b.Property(x => x.PreviewKey).HasMaxLength(200);
            b.Property(x => x.ThumbnailKey).HasMaxLength(200);
            b.Property(x => x.SupersedeReason).HasMaxLength(500);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.OwnerType, x.OwnerId });
            b.HasIndex(x => x.State);
            b.HasIndex(x => x.Sha256Server);
        });
    }
}
