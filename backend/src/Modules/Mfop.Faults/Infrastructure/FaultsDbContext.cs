using Mfop.Faults.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Faults.Infrastructure;

public sealed class FaultsDbContext(DbContextOptions<FaultsDbContext> options) : DbContext(options)
{
    public DbSet<Fault> Faults => Set<Fault>();
    public DbSet<FaultConfirmation> Confirmations => Set<FaultConfirmation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(FaultsModule.Info.Schema);

        modelBuilder.Entity<Fault>(b =>
        {
            b.ToTable("faults");
            b.HasKey(x => x.Id);
            b.Ignore(x => x.ReportedWithoutPower);
            b.Property(x => x.Category).HasMaxLength(30);
            b.Property(x => x.Source).HasMaxLength(20);
            b.Property(x => x.PowerAtInspection).HasMaxLength(30);
            b.Property(x => x.PowerInfoSource).HasMaxLength(30);
            b.Property(x => x.PowerNote).HasMaxLength(500);
            b.Property(x => x.ResponsibleParty).HasMaxLength(30);
            b.Property(x => x.ResponsibleEntityName).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.CaptureChannel).HasMaxLength(10);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.DismissReason).HasMaxLength(40);
            b.Property(x => x.DismissNote).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.State);
            b.HasIndex(x => new { x.AssetId, x.Category, x.State });
        });

        modelBuilder.Entity<FaultConfirmation>(b =>
        {
            b.ToTable("fault_confirmations");
            b.HasKey(x => x.Id);
            b.Property(x => x.PowerAtInspection).HasMaxLength(30);
            b.Property(x => x.PowerInfoSource).HasMaxLength(30);
            b.Property(x => x.PowerNote).HasMaxLength(500);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.CaptureChannel).HasMaxLength(10);
            b.HasIndex(x => x.FaultId);
            b.HasOne<Fault>().WithMany().HasForeignKey(x => x.FaultId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
