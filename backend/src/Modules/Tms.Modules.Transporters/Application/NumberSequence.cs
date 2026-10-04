using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Infrastructure.Persistence;

namespace Tms.Modules.Transporters.Application;

/// <summary>Hands out gap-tolerant, never-repeating numbers per tenant (TR-00001, TR-00002…) using one atomic SQL upsert.</summary>
internal sealed class NumberSequence(TransportersDbContext db)
{
    public async Task<long> NextAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO transporters_sequences (tenant_id, name, `value`) VALUES ({tenantId}, {name}, 1) ON DUPLICATE KEY UPDATE `value` = `value` + 1",
            cancellationToken);

        // Same transaction, so the row is locked and this reads exactly the value we just wrote.
        var value = await db.Database
            .SqlQuery<long>($"SELECT `value` AS Value FROM transporters_sequences WHERE tenant_id = {tenantId} AND name = {name}")
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return value;
    }
}
