using Mfop.Assets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Assets.Infrastructure;

public sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options) : DbContext(options)
{
    public const string AssetCodeSequence = "street_light_code_seq";

    public DbSet<StreetLight> StreetLights => Set<StreetLight>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<AssetVersion> AssetVersions => Set<AssetVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AssetsModule.Info.Schema);
        modelBuilder.HasSequence<long>(AssetCodeSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.Entity<StreetLight>(b =>
        {
            b.ToTable("street_lights");
            b.HasKey(x => x.Id);
            b.Property(x => x.AssetCode).HasMaxLength(20);
            b.HasIndex(x => x.AssetCode).IsUnique();
            b.Property(x => x.LifecycleState).HasMaxLength(20);
            b.Property(x => x.OperationalStatus).HasMaxLength(30);
            b.Property(x => x.Latitude).HasPrecision(9, 6);
            b.Property(x => x.Longitude).HasPrecision(9, 6);
            b.Property(x => x.AddressText).HasMaxLength(300);
            b.Property(x => x.CircuitRef).HasMaxLength(50);
            b.Property(x => x.PowerSource).HasMaxLength(10);
            b.Property(x => x.PoleMaterial).HasMaxLength(50);
            b.Property(x => x.PoleHeightM).HasPrecision(4, 1);
            b.Property(x => x.LuminaireMake).HasMaxLength(100);
            b.Property(x => x.LuminaireModel).HasMaxLength(100);
            b.Property(x => x.LuminaireSerial).HasMaxLength(100);
            b.Property(x => x.SolarBatteryType).HasMaxLength(50);
            b.Property(x => x.SolarControllerModel).HasMaxLength(100);
            b.Property(x => x.InstalledOnPrecision).HasMaxLength(10);
            b.Property(x => x.Supplier).HasMaxLength(200);
            b.Property(x => x.ResponsibleParty).HasMaxLength(30);
            b.Property(x => x.ResponsibleEntityName).HasMaxLength(200);
            b.Property(x => x.ObservedStatus).HasMaxLength(20);
            b.Property(x => x.Source).HasMaxLength(30);
            b.Property(x => x.CaptureChannel).HasMaxLength(10);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.LifecycleState);
        });

        modelBuilder.Entity<Registration>(b =>
        {
            b.ToTable("registrations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(30);
            b.Property(x => x.ProjectRef).HasMaxLength(100);
            b.Property(x => x.CaptureChannel).HasMaxLength(10);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.DecisionNote).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.State);
            b.HasIndex(x => x.RegistrarId);
            b.HasIndex(x => x.AssetId);
            b.HasOne<StreetLight>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Tag>(b =>
        {
            b.ToTable("tags");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(8).IsFixedLength();
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.CheckCode).HasMaxLength(2).IsFixedLength();
            b.Property(x => x.Status).HasMaxLength(20);
            b.Property(x => x.Origin).HasMaxLength(30);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.HumanCode);
            b.HasOne<StreetLight>().WithMany().HasForeignKey(x => x.BoundAssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AssetVersion>(b =>
        {
            b.ToTable("asset_versions");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.AssetId, x.VersionNo }).IsUnique();
            b.Property(x => x.Snapshot).HasColumnType("jsonb");
            b.Property(x => x.SnapshotHash).HasMaxLength(64).IsFixedLength();
            b.Property(x => x.Reason).HasMaxLength(100);
            b.HasOne<StreetLight>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
