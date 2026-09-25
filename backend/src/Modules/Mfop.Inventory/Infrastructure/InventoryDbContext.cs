using Mfop.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Inventory.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemCost> ItemCosts => Set<ItemCost>();
    public DbSet<StockLocation> Locations => Set<StockLocation>();
    public DbSet<StockMovement> Movements => Set<StockMovement>();
    public DbSet<StockBalance> Balances => Set<StockBalance>();
    public DbSet<StockIssue> Issues => Set<StockIssue>();
    public DbSet<StockReturn> Returns => Set<StockReturn>();
    public DbSet<CoreReturn> CoreReturns => Set<CoreReturn>();
    public DbSet<SerialUnit> SerialUnits => Set<SerialUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(InventoryModule.Info.Schema);

        modelBuilder.Entity<Item>(b =>
        {
            b.ToTable("items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Sku).HasMaxLength(40);
            b.HasIndex(x => x.Sku).IsUnique();
            b.Property(x => x.NameAr).HasMaxLength(200);
            b.Property(x => x.NameEn).HasMaxLength(200);
            b.Property(x => x.Unit).HasMaxLength(4);
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<ItemCost>(b =>
        {
            b.ToTable("item_costs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(19, 4);
            b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            b.HasIndex(x => new { x.ItemId, x.AsOf });
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockLocation>(b =>
        {
            b.ToTable("stock_locations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(20);
            b.Property(x => x.Name).HasMaxLength(200);
            b.HasIndex(x => new { x.Kind, x.CustodianUserId }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<StockMovement>(b =>
        {
            b.ToTable("stock_movements");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(20);
            b.Property(x => x.Quantity).HasPrecision(12, 3);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.HasIndex(x => x.WorkOrderId);
            b.HasIndex(x => x.AssetId);
            b.HasIndex(x => x.ItemId);
            b.HasIndex(x => x.ReversalOf).IsUnique();
            b.HasIndex(x => x.DocumentId);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("ck_stock_movements_quantity_positive", "quantity > 0"));
        });

        // M5 documents: numbered for people (ISS-000001, RET-000001, CORE-000001).
        modelBuilder.HasSequence<long>("stock_issue_code_seq");
        modelBuilder.HasSequence<long>("stock_return_code_seq");
        modelBuilder.HasSequence<long>("core_return_code_seq");

        modelBuilder.Entity<StockIssue>(b =>
        {
            b.ToTable("stock_issues");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.RequestedBy).HasMaxLength(200);
            b.Property(x => x.DisputeReason).HasMaxLength(1000);
            b.Property(x => x.CancelReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.TechnicianId, x.State });
            b.HasIndex(x => x.State);
        });

        modelBuilder.Entity<StockReturn>(b =>
        {
            b.ToTable("stock_returns");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Kind).HasMaxLength(20);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.RejectReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.TechnicianId, x.State });
            b.HasIndex(x => x.State);
        });

        modelBuilder.Entity<CoreReturn>(b =>
        {
            b.ToTable("core_returns");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(20);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.State).HasMaxLength(30);
            b.Property(x => x.WorkOrderCode).HasMaxLength(40);
            b.Property(x => x.AssetCode).HasMaxLength(40);
            b.Property(x => x.Quantity).HasPrecision(12, 3);
            b.Property(x => x.InstalledSerial).HasMaxLength(60);
            b.Property(x => x.ExpectedSerial).HasMaxLength(60);
            b.Property(x => x.DeclaredSerial).HasMaxLength(60);
            b.Property(x => x.ReceivedSerial).HasMaxLength(60);
            b.Property(x => x.DeclaredReason).HasMaxLength(1000);
            b.Property(x => x.NotPresentedReason).HasMaxLength(1000);
            b.Property(x => x.WaiveReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.ReferenceSerial);
            b.HasIndex(x => x.WorkOrderId);
            b.HasIndex(x => x.AssetId);
            b.HasIndex(x => new { x.TechnicianId, x.State });
            b.HasIndex(x => x.State);
            b.HasIndex(x => x.ConsumeMovementId);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StockMovement>().WithMany().HasForeignKey(x => x.ConsumeMovementId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("ck_core_returns_quantity_positive", "quantity > 0"));
        });

        modelBuilder.Entity<SerialUnit>(b =>
        {
            b.ToTable("serial_units");
            b.HasKey(x => x.Id);
            b.Property(x => x.Serial).HasMaxLength(60);
            b.HasIndex(x => new { x.ItemId, x.Serial }).IsUnique();
            b.HasIndex(x => new { x.InstalledAssetId, x.ItemId });
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockBalance>(b =>
        {
            b.ToTable("stock_balances");
            b.HasKey(x => x.Id);
            b.Property(x => x.Quantity).HasPrecision(12, 3);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.LocationId, x.ItemId }).IsUnique();
            b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
