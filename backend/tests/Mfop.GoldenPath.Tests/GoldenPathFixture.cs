using Mfop.Api.Tests;
using Mfop.Migrator;
using Microsoft.Extensions.Configuration;

namespace Mfop.GoldenPath.Tests;

/// <summary>A fresh database set up exactly like <c>make seed</c>: migrate, reference data, dev users, then the synthetic DEMO 1 seed.</summary>
public sealed class GoldenPathFixture : IAsyncLifetime
{
    public DatabaseFixture Db { get; } = new();

    public async Task InitializeAsync()
    {
        await Db.InitializeAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tags:CheckKey"] = TestApp.TagCheckKey,
            ["Tags:BaseUrl"] = "http://web.test/t",
            ["Storage:Mode"] = "local",
            ["Storage:Root"] = Db.ObjectStoreRoot,
        }).Build();
        await new DemoSeeder(Db.OwnerConnectionString, configuration, TextWriter.Null).SeedAsync(DatabaseFixture.DemoDataPath, CancellationToken.None);
    }

    public Task DisposeAsync() => Db.DisposeAsync();
}
