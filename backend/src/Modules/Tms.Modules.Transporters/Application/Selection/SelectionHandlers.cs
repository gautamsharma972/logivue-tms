using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Performance;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Selection;

public sealed record CapabilityDto(Guid Id, Guid TransporterId, string Code, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record AddCapabilityRequest(string Code, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record CapabilityTypeDto(string Code, string Name);

public sealed record PlanningRuleDto(
    Guid Id, Guid TransporterId, PlanningRuleType RuleType, Guid? LaneId, string Reason, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? EndedBecause);

public sealed record AddPlanningRuleRequest(PlanningRuleType RuleType, Guid? LaneId, string Reason, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record EndPlanningRuleRequest(string Reason);

/// <summary>Who may take a load, and who should. Staff only: it shows other carriers' performance and the planning decisions about them.</summary>
internal sealed class SelectionHandler(SelectionService selection, PerformanceAccess access, FluentValidation.IValidator<SelectionRequest> validator)
{
    public async Task<Result<IReadOnlyList<CandidateEvaluation>>> EligibilityAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanSelect)
        {
            return PerformanceAccess.Forbidden;
        }

        var invalid = Check(request);
        return invalid is not null ? invalid : Result.Success(await selection.CheckAsync(request, cancellationToken));
    }

    public async Task<Result<RecommendationResult>> RecommendAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanSelect)
        {
            return PerformanceAccess.Forbidden;
        }

        var invalid = Check(request);
        return invalid is not null ? invalid : await selection.RecommendAsync(request, cancellationToken);
    }

    private Error? Check(SelectionRequest request)
    {
        var result = validator.Validate(request);
        if (result.IsValid)
        {
            return null;
        }

        return Error.Validation("validation.failed", "One or more fields are invalid.") with
        {
            ValidationErrors = result.Errors.GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..]).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
        };
    }
}

internal sealed class CapabilityHandler(TransportersDbContext db, PerformanceAccess access, ICurrentUser user, TimeProvider clock)
{
    public static IReadOnlyList<CapabilityTypeDto> Catalog() => CapabilityCatalog.Items.Select(i => new CapabilityTypeDto(i.Code, i.Name)).ToList();

    public async Task<Result<IReadOnlyList<CapabilityDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var items = await db.Capabilities.AsNoTracking().Where(c => c.TransporterId == transporterId).OrderBy(c => c.Code).ThenByDescending(c => c.EffectiveFrom).ToListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<Result<CapabilityDto>> AddAsync(Guid transporterId, AddCapabilityRequest request, CancellationToken cancellationToken)
    {
        if (access.CheckWrite(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken) || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        var capability = TransporterCapability.Create(tenantId, transporterId, request.Code, request.EffectiveFrom, request.EffectiveTo);
        if (capability.IsFailure)
        {
            return capability.Error;
        }

        if (await db.Capabilities.AnyAsync(c => c.TransporterId == transporterId && c.Code == capability.Value.Code && c.IsActive && c.EffectiveTo == null, cancellationToken))
        {
            return Error.Conflict("capabilities.duplicate", $"{capability.Value.Code} is already held by this transporter.");
        }

        db.Capabilities.Add(capability.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(capability.Value);
    }

    public async Task<Result<CapabilityDto>> EndAsync(Guid capabilityId, CancellationToken cancellationToken)
    {
        var capability = await db.Capabilities.FirstOrDefaultAsync(c => c.Id == capabilityId, cancellationToken);
        if (capability is null || access.CheckWrite(capability.TransporterId).IsFailure)
        {
            return Error.NotFound("capabilities.not_found", "Capability not found.");
        }

        var ended = capability.End(clock.TodayInIndia());
        if (ended.IsFailure)
        {
            return ended.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(capability);
    }

    private static CapabilityDto ToDto(TransporterCapability c) =>
        new(c.Id, c.TransporterId, c.Code, CapabilityCatalog.Items.FirstOrDefault(i => i.Code == c.Code).Name ?? c.Code, c.EffectiveFrom, c.EffectiveTo, c.IsActive);
}

internal sealed class PlanningRuleHandler(TransportersDbContext db, PerformanceAccess access, ICurrentUser user, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<PlanningRuleDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (!access.CanSeeAll)
        {
            return PerformanceAccess.Forbidden; // a vendor never sees the internal decisions made about it
        }

        var rules = await db.PlanningRules.AsNoTracking().Where(r => r.TransporterId == transporterId).OrderByDescending(r => r.IsActive).ThenByDescending(r => r.EffectiveFrom).ToListAsync(cancellationToken);
        return rules.Select(ToDto).ToList();
    }

    public async Task<Result<PlanningRuleDto>> AddAsync(Guid transporterId, AddPlanningRuleRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken) || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        if (request.LaneId is { } laneId && !await db.Lanes.AnyAsync(l => l.Id == laneId && l.TransporterId == transporterId, cancellationToken))
        {
            return Error.NotFound("lanes.not_found", "That lane does not belong to this transporter.");
        }

        var rule = PlanningRule.Create(tenantId, transporterId, request.RuleType, request.LaneId, request.Reason, request.EffectiveFrom, request.EffectiveTo);
        if (rule.IsFailure)
        {
            return rule.Error;
        }

        db.PlanningRules.Add(rule.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(rule.Value);
    }

    public async Task<Result<PlanningRuleDto>> EndAsync(Guid ruleId, EndPlanningRuleRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        var rule = await db.PlanningRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return Error.NotFound("planning_rules.not_found", "Planning rule not found.");
        }

        var ended = rule.End(request.Reason, clock.TodayInIndia());
        if (ended.IsFailure)
        {
            return ended.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(rule);
    }

    private static PlanningRuleDto ToDto(PlanningRule r) => new(r.Id, r.TransporterId, r.RuleType, r.LaneId, r.Reason, r.EffectiveFrom, r.EffectiveTo, r.IsActive, r.EndedBecause);
}
