using Npgsql;

namespace Mfop.Infrastructure.Persistence;

/// <summary>
/// One PostgreSQL connection per request/command scope, shared by every module DbContext and the audit appender,
/// so that entity changes and their audit events commit (or roll back) in ONE transaction (ADR-0007).
/// </summary>
public sealed class DbSession(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public NpgsqlTransaction? Transaction { get; internal set; }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
