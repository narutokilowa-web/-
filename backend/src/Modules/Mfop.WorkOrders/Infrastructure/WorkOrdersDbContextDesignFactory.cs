using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.WorkOrders.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`. Never used at runtime.</summary>
internal sealed class WorkOrdersDbContextDesignFactory : IDesignTimeDbContextFactory<WorkOrdersDbContext>
{
    public WorkOrdersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkOrdersDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", WorkOrdersModule.Info.Schema)
            .Options;
        return new WorkOrdersDbContext(options);
    }
}
