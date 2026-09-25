using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.Audit.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`.</summary>
internal sealed class AuditDbContextDesignFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", AuditModule.Info.Schema)
            .Options);
}
