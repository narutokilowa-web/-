using Mfop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mfop.Rules.Infrastructure;

/// <summary>Used only by `dotnet ef migrations add`. Never used at runtime.</summary>
internal sealed class RulesDbContextDesignFactory : IDesignTimeDbContextFactory<RulesDbContext>
{
    public RulesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseMfopPostgres("Host=localhost;Database=mfop_design;Username=design;Password=design", RulesModule.Info.Schema)
            .Options;
        return new RulesDbContext(options);
    }
}
