using System.Data;
using AeroAuth.Core.Interfaces;
using Dapper;

namespace AeroAuth.Infrastructure.Repositories;

/// <summary>
/// Base for every repository. All Dapper calls go through these helpers, which attach the unit of work's current
/// transaction, so a repository call takes part in the caller's transaction without knowing about it.
/// </summary>
public abstract class BaseRepository(IUnitOfWork uow)
{
    protected IDbConnection Connection => uow.Connection;
    protected IDbTransaction? Transaction => uow.Transaction;

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null) =>
        (await Connection.QueryAsync<T>(new CommandDefinition(sql, param, Transaction))).ToList();

    protected Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null) =>
        Connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, param, Transaction));

    protected Task<int> ExecuteAsync(string sql, object? param = null) =>
        Connection.ExecuteAsync(new CommandDefinition(sql, param, Transaction));

    protected Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null) =>
        Connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, param, Transaction));
}
