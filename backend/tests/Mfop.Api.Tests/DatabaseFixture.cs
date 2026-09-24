using Mfop.Migrator;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Mfop.Api.Tests;

/// <summary>
/// Real PostGIS container initialised with the same init script as Docker Compose,
/// migrated and seeded with the same migrator and dev users.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string OwnerPassword = "test-owner-pw";
    public const string AppPassword = "test-app-pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:17-3.5")
        .WithUsername("postgres")
        .WithPassword("test-superuser-pw")
        .WithDatabase("postgres")
        .WithEnvironment("MFOP_OWNER_PASSWORD", OwnerPassword)
        .WithEnvironment("MFOP_APP_PASSWORD", AppPassword)
        .WithEnvironment("MFOP_READONLY_PASSWORD", "test-ro-pw")
        .WithEnvironment("KEYCLOAK_DB_PASSWORD", "test-kc-pw")
        .WithResourceMapping(new FileInfo(RepoPaths.Path("deploy", "postgres", "init", "01-roles-and-databases.sh")), "/docker-entrypoint-initdb.d/")
        .Build();

    public string OwnerConnectionString => Connection("mfop_owner", OwnerPassword);

    public string AppConnectionString => Connection("mfop_app", AppPassword);

    public static string DevUsersPath => RepoPaths.Path("deploy", "seed", "dev-users.json");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var runner = new MigrationRunner(OwnerConnectionString, TextWriter.Null);
        await runner.MigrateAsync(CancellationToken.None);
        await runner.SeedDevUsersAsync(DevUsersPath, CancellationToken.None);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task ExecuteAsOwnerAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsOwnerAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public string ReadOnlyConnectionString => Connection("mfop_readonly", "test-ro-pw");

    private string Connection(string user, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = "mfop",
            Username = user,
            Password = password,
        }.ConnectionString;
}

[CollectionDefinition(nameof(DatabaseCollection))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
