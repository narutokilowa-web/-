using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.Sync.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`. Never used at runtime.</summary>
internal sealed class SyncDbContextDesignFactory : IDesignTimeDbContextFactory<SyncDbContext>
{
    public SyncDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", SyncModule.Info.Schema)
            .Options;
        return new SyncDbContext(options);
    }
}
