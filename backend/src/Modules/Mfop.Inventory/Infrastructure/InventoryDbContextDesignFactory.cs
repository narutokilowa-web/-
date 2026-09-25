using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.Inventory.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`. Never used at runtime.</summary>
internal sealed class InventoryDbContextDesignFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", InventoryModule.Info.Schema)
            .Options;
        return new InventoryDbContext(options);
    }
}
