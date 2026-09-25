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
            b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("ck_stock_movements_quantity_positive", "quantity > 0"));
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
