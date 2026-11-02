using System.Data;
using System.Data.Common;

namespace AeroAuth.Core.Interfaces;

public interface IDbConnectionFactory
{
    DbConnection Create();
}

/// <summary>
/// Owns one connection and at most one transaction per request. Repositories take both from here, so every repository
/// call made inside ExecuteInTransactionAsync takes part in the same transaction and commits or rolls back together.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IDbConnection Connection { get; }
    IDbTransaction? Transaction { get; }
    bool InTransaction { get; }

    Task BeginAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
    Task CommitAsync();
    Task RollbackAsync();

    /// <summary>Runs the work in a transaction: commit on success, rollback on any exception. Joins an active transaction.</summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work);
    Task ExecuteInTransactionAsync(Func<Task> work);
}

/// <summary>Who is calling, and which organization their data is limited to.</summary>
public interface ITenantContext
{
    int UserId { get; }
    string Role { get; }
    int? OrganizationId { get; }
    bool IsAdmin { get; }
    bool IsExternalAmo { get; }

    /// <summary>
    /// The organization every tenant query is limited to. External AMO users always get their own organization.
    /// Internal QA users get null, which means "all organizations". Throws TenantViolationException if an AMO user has no organization.
    /// </summary>
    int? ResolveScope();
}
