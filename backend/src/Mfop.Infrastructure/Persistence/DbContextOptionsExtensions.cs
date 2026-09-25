using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Infrastructure.Persistence;

public static class DbContextOptionsExtensions
{
    /// <summary>
    /// Standard PostgreSQL configuration for every module: snake_case names and a per-module migrations history table.
    /// </summary>
    public static DbContextOptionsBuilder UseMfopPostgres(this DbContextOptionsBuilder builder, string connectionString, string schema) =>
        builder
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();

    /// <summary>Runtime configuration: use the scope's shared connection (see <see cref="DbSession"/>).</summary>
    public static DbContextOptionsBuilder UseMfopPostgres(this DbContextOptionsBuilder builder, DbConnection connection, string schema) =>
        builder
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();

    public static DbContextOptionsBuilder<T> UseMfopPostgres<T>(this DbContextOptionsBuilder<T> builder, string connectionString, string schema)
        where T : DbContext =>
        (DbContextOptionsBuilder<T>)((DbContextOptionsBuilder)builder).UseMfopPostgres(connectionString, schema);
}
