using Microsoft.EntityFrameworkCore;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Platform.Infrastructure.Persistence;

/// <summary>Hands out never-repeating numbers per tenant and name using one atomic upsert in its own short transaction.</summary>
internal sealed class SequenceGenerator(PlatformDbContext db) : ISequenceGenerator
{
    public async Task<long> NextAsync(Guid tenantId, string name, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO platform_sequences (tenant_id, name, `value`) VALUES ({tenantId}, {name}, 1) ON DUPLICATE KEY UPDATE `value` = `value` + 1",
            cancellationToken);

        // Same transaction, so the row is locked and this reads exactly the value just written.
        var value = await db.Database
            .SqlQuery<long>($"SELECT `value` AS Value FROM platform_sequences WHERE tenant_id = {tenantId} AND name = {name}")
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return value;
    }
}
