using Mfop.WorkOrders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Infrastructure;

public sealed class WorkOrdersDbContext(DbContextOptions<WorkOrdersDbContext> options) : DbContext(options)
{
    public const string CodeSequence = "work_order_code_seq";

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignment> Assignments => Set<WorkOrderAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(WorkOrdersModule.Info.Schema);
        modelBuilder.HasSequence<long>(CodeSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.Entity<WorkOrder>(b =>
        {
            b.ToTable("work_orders");
            b.HasKey(x => x.Id);
            b.Ignore(x => x.IsTerminal);
            b.Property(x => x.Code).HasMaxLength(20);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Priority).HasMaxLength(2);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.CancelReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => x.State);
            b.HasIndex(x => x.FaultId);
            b.HasIndex(x => x.AssetId);
            b.HasIndex(x => new { x.AssigneeId, x.State });
        });

        modelBuilder.Entity<WorkOrderAssignment>(b =>
        {
            b.ToTable("assignments");
            b.HasKey(x => x.Id);
            b.Property(x => x.State).HasMaxLength(10);
            b.Property(x => x.EndReason).HasMaxLength(1000);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.WorkOrderId, x.State });
            b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
