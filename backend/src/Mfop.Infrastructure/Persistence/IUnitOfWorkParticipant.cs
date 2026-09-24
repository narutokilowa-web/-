using Microsoft.EntityFrameworkCore;

namespace Mfop.Infrastructure.Persistence;

/// <summary>A module DbContext whose changes are committed (and audited) by the unit of work.</summary>
public interface IUnitOfWorkParticipant
{
    DbContext Context { get; }
}

public sealed class UnitOfWorkParticipant<TContext>(TContext context) : IUnitOfWorkParticipant
    where TContext : DbContext
{
    public DbContext Context => context;
}
