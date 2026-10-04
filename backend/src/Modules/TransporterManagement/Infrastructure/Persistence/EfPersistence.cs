using System.Linq.Expressions;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.Shared.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;

internal sealed class EfRepository<T>(TransporterDbContext db) : IRepository<T> where T : class
{
    private DbSet<T> Set => db.Set<T>();

    public async Task<T?> FindAsync(long id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken);

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(predicate, cancellationToken);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Set.Where(predicate).ToListAsync(cancellationToken);

    public void Add(T entity) => Set.Add(entity);

    public void Remove(T entity) => Set.Remove(entity);

    public async Task<PagedResult<T>> PageAsync(Expression<Func<T, bool>> predicate, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Set.Where(predicate);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => EF.Property<long>(e, "Id"))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, total);
    }
}

internal sealed class EfUnitOfWork(TransporterDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransaction(transaction);
    }

    private sealed class EfTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
