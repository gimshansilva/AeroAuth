using System.Data;
using System.Data.Common;
using AeroAuth.Core.Interfaces;

namespace AeroAuth.Infrastructure.Data;

/// <summary>
/// One connection and at most one transaction per scope (per HTTP request). Registered as scoped, so every repository
/// in the request shares the same connection and transaction.
/// </summary>
public sealed class UnitOfWork(IDbConnectionFactory factory) : IUnitOfWork
{
    private readonly DbConnection _connection = factory.Create();
    private DbTransaction? _transaction;

    public IDbConnection Connection => _connection;
    public IDbTransaction? Transaction => _transaction;
    public bool InTransaction => _transaction is not null;

    public async Task BeginAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        if (_transaction is not null) throw new InvalidOperationException("A transaction is already active.");
        if (_connection.State != ConnectionState.Open) await _connection.OpenAsync();
        _transaction = await _connection.BeginTransactionAsync(isolationLevel);
    }

    public async Task CommitAsync()
    {
        if (_transaction is null) throw new InvalidOperationException("There is no active transaction to commit.");
        try { await _transaction.CommitAsync(); }
        finally { await _transaction.DisposeAsync(); _transaction = null; }
    }

    public async Task RollbackAsync()
    {
        if (_transaction is null) return;
        try { await _transaction.RollbackAsync(); }
        finally { await _transaction.DisposeAsync(); _transaction = null; }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work)
    {
        if (_transaction is not null) return await work();   // join the transaction that is already running

        await BeginAsync();
        try
        {
            var result = await work();
            await CommitAsync();
            return result;
        }
        catch
        {
            await RollbackAsync();
            throw;
        }
    }

    public Task ExecuteInTransactionAsync(Func<Task> work) =>
        ExecuteInTransactionAsync<bool>(async () => { await work(); return true; });

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await RollbackAsync();   // never leave a transaction open
        await _connection.DisposeAsync();
    }
}
