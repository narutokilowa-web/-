using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.Faults.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`. Never used at runtime.</summary>
internal sealed class FaultsDbContextDesignFactory : IDesignTimeDbContextFactory<FaultsDbContext>
{
    public FaultsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FaultsDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", FaultsModule.Info.Schema)
            .Options;
        return new FaultsDbContext(options);
    }
}
