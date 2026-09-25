using Mfop.WorkOrders.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Infrastructure;

public sealed class WorkOrdersDbContext(DbContextOptions<WorkOrdersDbContext> options) : DbContext(options)
{
    public const string CodeSequence = "work_order_code_seq";

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderAssignment> Assignments => Set<WorkOrderAssignment>();
    public DbSet<WorkOrderEvent> Events => Set<WorkOrderEvent>();
    public DbSet<WorkOrderArrival> Arrivals => Set<WorkOrderArrival>();
    public DbSet<DiagnosisCode> DiagnosisCodes => Set<DiagnosisCode>();
    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();
    public DbSet<ChecklistResult> ChecklistResults => Set<ChecklistResult>();
    public DbSet<WorkOrderSubmission> Submissions => Set<WorkOrderSubmission>();
    public DbSet<WorkOrderReview> Reviews => Set<WorkOrderReview>();

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
            b.Property(x => x.DiagnosisCode).HasMaxLength(40);
            b.Property(x => x.DiagnosisNote).HasMaxLength(1000);
            b.Property(x => x.RootCauseNote).HasMaxLength(1000);
            b.Property(x => x.NoMaterialReason).HasMaxLength(1000);
            b.Property(x => x.TestMethod).HasMaxLength(30);
            b.Property(x => x.TestResult).HasMaxLength(20);
            b.Property(x => x.Outcome).HasMaxLength(20);
            b.Property(x => x.SealHash).HasMaxLength(64).IsFixedLength();
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

        modelBuilder.Entity<WorkOrderEvent>(b =>
        {
            b.ToTable("wo_events");
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(40);
            b.Property(x => x.Data).HasColumnType("jsonb");
            b.HasIndex(x => new { x.WorkOrderId, x.Seq }).IsUnique();
            b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrderArrival>(b =>
        {
            b.ToTable("wo_arrivals");
            b.HasKey(x => x.Id);
            b.Property(x => x.ScannedCode).HasMaxLength(200);
            b.Property(x => x.ManualCode).HasMaxLength(60);
            b.Property(x => x.ManualReason).HasMaxLength(500);
            b.HasIndex(x => new { x.WorkOrderId, x.ArrivalNo }).IsUnique();
            b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DiagnosisCode>(b =>
        {
            b.ToTable("diagnosis_codes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.NameAr).HasMaxLength(200);
            b.Property(x => x.NameEn).HasMaxLength(200);
            b.Property(x => x.Path).HasMaxLength(20);
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<ChecklistTemplate>(b =>
        {
            b.ToTable("checklist_templates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(60);
            b.HasIndex(x => new { x.Code, x.TemplateVersion }).IsUnique();
            b.Property(x => x.Items).HasColumnType("jsonb");
            b.Property(x => x.TestMethods).HasColumnType("jsonb");
            b.Property(x => x.EvidenceRequirements).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ChecklistResult>(b =>
        {
            b.ToTable("checklist_results");
            b.HasKey(x => x.Id);
            b.Property(x => x.TestMethod).HasMaxLength(30);
            b.Property(x => x.NotTestedReason).HasMaxLength(500);
            b.Property(x => x.Answers).HasColumnType("jsonb");
            b.Property(x => x.TestResult).HasMaxLength(20);
            b.HasIndex(x => x.WorkOrderId);
            b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ChecklistTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrderSubmission>(b =>
        {
            b.ToTable("wo_submissions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Report).HasColumnType("jsonb");
            b.Property(x => x.PayloadHash).HasMaxLength(64).IsFixedLength();
            b.HasIndex(x => new { x.WorkOrderId, x.SubmissionNo }).IsUnique();
            b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkOrderReview>(b =>
        {
            b.ToTable("wo_reviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.Decision).HasMaxLength(10);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.HasIndex(x => x.WorkOrderId);
            b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WorkOrderSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
