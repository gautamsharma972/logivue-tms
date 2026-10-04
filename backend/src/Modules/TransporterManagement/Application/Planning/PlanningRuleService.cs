using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Planning;

public sealed record PlanningRuleRequest(
    PlanningRuleType RuleType,
    long? LaneReference,
    string Reason,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

public sealed record PlanningRuleDto(
    long Id,
    long TransporterId,
    PlanningRuleType RuleType,
    long? LaneReference,
    string Reason,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive,
    string CreatedBy);

public interface IPlanningRuleService
{
    Task<IReadOnlyList<PlanningRuleDto>> ListAsync(long transporterId, CancellationToken cancellationToken = default);

    Task<PlanningRuleDto> AddAsync(long transporterId, PlanningRuleRequest request, CancellationToken cancellationToken = default);

    Task<PlanningRuleDto> DeactivateAsync(long transporterId, long ruleId, ReasonRequest request, CancellationToken cancellationToken = default);
}

public sealed class PlanningRuleService(
    IRepository<Transporter> transporters,
    IRepository<TransporterLane> lanes,
    IRepository<TransporterPlanningRule> rules,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<PlanningRuleRequest> ruleValidator,
    IValidator<ReasonRequest> reasonValidator) : IPlanningRuleService
{
    public async Task<IReadOnlyList<PlanningRuleDto>> ListAsync(long transporterId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        return (await rules.ListAsync(r => r.TransporterId == transporterId, cancellationToken))
            .OrderByDescending(r => r.Id)
            .Select(ToDto)
            .ToList();
    }

    public async Task<PlanningRuleDto> AddAsync(long transporterId, PlanningRuleRequest request, CancellationToken cancellationToken = default)
    {
        await ruleValidator.ValidateAndThrowAsync(request, cancellationToken);

        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        EnsureManager();

        if (request.LaneReference is { } laneId && !await lanes.AnyAsync(l => l.Id == laneId && l.TransporterId == transporterId, cancellationToken))
        {
            throw new NotFoundException($"Lane {laneId} was not found for transporter {transporterId}.", "LANE_NOT_FOUND");
        }

        var entity = new TransporterPlanningRule
        {
            TransporterId = transporterId,
            RuleType = request.RuleType,
            LaneReference = request.LaneReference,
            Reason = request.Reason.Trim(),
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = true,
            CreatedBy = currentUser.UserId
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        rules.Add(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterPlanningRule", entity.Id.ToString(), "PlanningRuleAdded",
            NewValueJson: AuditJson.Serialize(entity), Reason: entity.Reason), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(entity);
    }

    public async Task<PlanningRuleDto> DeactivateAsync(long transporterId, long ruleId, ReasonRequest request, CancellationToken cancellationToken = default)
    {
        await reasonValidator.ValidateAndThrowAsync(request, cancellationToken);

        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);
        EnsureManager();

        var entity = await rules.FindAsync(ruleId, cancellationToken);
        if (entity is null || entity.TransporterId != transporterId)
        {
            throw new NotFoundException($"Planning rule {ruleId} was not found for transporter {transporterId}.", "PLANNING_RULE_NOT_FOUND");
        }

        if (!entity.IsActive)
        {
            throw new BusinessRuleException("The planning rule is already inactive.", "ILLEGAL_TRANSITION");
        }

        var before = AuditJson.Serialize(entity);
        entity.IsActive = false;
        entity.EffectiveTo = clock.GetUtcNow().UtcDateTime.Date;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterPlanningRule", ruleId.ToString(), "PlanningRuleDeactivated",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(entity), Reason: request.Reason.Trim()), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToDto(entity);
    }

    /// <summary>Planning classifications change who gets loads, so only managers and administrators may set them.</summary>
    private void EnsureManager()
    {
        if (!currentUser.IsInRole(Roles.TransportManager) && !currentUser.IsInRole(Roles.TransportAdmin))
        {
            throw new ForbiddenException("Only transport managers can change planning rules.", "WORKFLOW_ROLE_REQUIRED");
        }
    }

    private static PlanningRuleDto ToDto(TransporterPlanningRule r) =>
        new(r.Id, r.TransporterId, r.RuleType, r.LaneReference, r.Reason, r.EffectiveFrom, r.EffectiveTo, r.IsActive, r.CreatedBy);
}
