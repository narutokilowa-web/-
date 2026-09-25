using Mfop.Rules.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Rules.Infrastructure;

public sealed class RulesDbContext(DbContextOptions<RulesDbContext> options) : DbContext(options)
{
    public DbSet<RuleSetting> Settings => Set<RuleSetting>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Flag> Flags => Set<Flag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(RulesModule.Info.Schema);

        modelBuilder.Entity<RuleSetting>(b =>
        {
            b.ToTable("rule_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.RuleCode).HasMaxLength(40);
            b.HasIndex(x => x.RuleCode).IsUnique();
            b.Property(x => x.Params).HasColumnType("jsonb");
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Recommendation>(b =>
        {
            b.ToTable("recommendations");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(40);
            b.Property(x => x.RuleCode).HasMaxLength(40);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.Explanation).HasColumnType("jsonb");
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.AssetId, x.State });
        });

        modelBuilder.Entity<Flag>(b =>
        {
            b.ToTable("flags");
            b.HasKey(x => x.Id);
            b.Property(x => x.RuleCode).HasMaxLength(40);
            b.Property(x => x.Severity).HasMaxLength(10);
            b.Property(x => x.SubjectType).HasMaxLength(40);
            b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.Explanation).HasColumnType("jsonb");
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasIndex(x => new { x.SubjectType, x.SubjectId });
        });
    }
}
