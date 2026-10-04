using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application.Planning;

/// <summary>Maintains which product categories must never share a vehicle (the planner enforces them).</summary>
internal sealed class CompatibilityRulesHandler(ShipmentsDbContext db, ShipmentAccess access, ICurrentUser user)
{
    private static CompatibilityRuleDto ToDto(ProductCompatibilityRule r) => new(r.Id, r.CategoryA, r.CategoryB, r.Reason);

    public async Task<Result<IReadOnlyList<CompatibilityRuleDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var rules = await db.CompatibilityRules.AsNoTracking().OrderBy(r => r.CategoryA).ThenBy(r => r.CategoryB).ToListAsync(cancellationToken);
        return rules.Select(ToDto).ToList();
    }

    public async Task<Result<CompatibilityRuleDto>> CreateAsync(SaveCompatibilityRuleRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanPlan || user.TenantId is not { } tenantId)
        {
            return ShipmentAccess.Forbidden;
        }

        var rule = ProductCompatibilityRule.Create(tenantId, request.CategoryA, request.CategoryB, request.Reason);
        if (rule.IsFailure)
        {
            return rule.Error;
        }

        if (await db.CompatibilityRules.AnyAsync(r => r.CategoryA == rule.Value.CategoryA && r.CategoryB == rule.Value.CategoryB, cancellationToken))
        {
            return Error.Conflict("compatibility.duplicate", "These two categories are already marked as incompatible.");
        }

        db.CompatibilityRules.Add(rule.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(rule.Value);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanPlan)
        {
            return ShipmentAccess.Forbidden;
        }

        var rule = await db.CompatibilityRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return Error.NotFound("compatibility.not_found", "That rule does not exist.");
        }

        db.CompatibilityRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
