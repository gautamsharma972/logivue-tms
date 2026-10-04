using System.Linq.Expressions;
using LogiVue.Tms.Shared.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Abstractions;

/// <summary>Write-side access to a single aggregate or child entity type.</summary>
public interface IRepository<T> where T : class
{
    Task<T?> FindAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    void Add(T entity);

    void Remove(T entity);

    /// <summary>One bounded page, newest first. Use this for any list that can grow.</summary>
    Task<PagedResult<T>> PageAsync(Expression<Func<T, bool>> predicate, int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Commits pending changes. Services wrap a business change and its audit rows in one
/// <see cref="BeginTransactionAsync"/> scope so they commit together or not at all.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>An open transaction. Disposing without <see cref="CommitAsync"/> rolls back.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
