using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.Evidence.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`. Never used at runtime.</summary>
internal sealed class EvidenceDbContextDesignFactory : IDesignTimeDbContextFactory<EvidenceDbContext>
{
    public EvidenceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EvidenceDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", EvidenceModule.Info.Schema)
            .Options;
        return new EvidenceDbContext(options);
    }
}
