using FluentValidation;
using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Performance;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Claims;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Claims;

public sealed record RecordClaimRequest(ClaimType ClaimType, DateOnly ClaimDate, decimal ClaimValue, string? LoadReference, string? Remarks);

public sealed record ClaimDto(
    long Id,
    long TransporterId,
    string? LoadReference,
    ClaimType ClaimType,
    DateOnly ClaimDate,
    decimal ClaimValue,
    ClaimStatus Status,
    string? Remarks,
    DateTime? ResolvedAt);

public interface IClaimService
{
    Task<IReadOnlyList<ClaimDto>> ListAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<ClaimDto> RecordAsync(long transporterId, RecordClaimRequest request, CancellationToken cancellationToken = default);

    Task<ClaimDto> ResolveAsync(long transporterId, long claimId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Claims recorded against a transporter. Each change rebuilds the claims KPI for the claim's month, so the scorecard
/// follows the claim without anyone editing a number by hand.
/// </summary>
public sealed class ClaimService(
    IRepository<Transporter> transporters,
    IRepository<TransporterClaim> claims,
    IPerformanceService performance,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<RecordClaimRequest> validator) : IClaimService
{
    public async Task<IReadOnlyList<ClaimDto>> ListAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        return (await claims.ListAsync(c => c.TransporterId == transporterId && c.ClaimDate >= from && c.ClaimDate <= to, cancellationToken))
            .OrderByDescending(c => c.ClaimDate).ThenByDescending(c => c.Id).Select(ToDto).ToList();
    }

    public async Task<ClaimDto> RecordAsync(long transporterId, RecordClaimRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (request.ClaimDate > today)
        {
            throw new BusinessRuleException("A claim cannot be dated in the future.", "CLAIM_DATE_INVALID");
        }

        var claim = new TransporterClaim
        {
            TransporterId = transporterId,
            LoadReference = request.LoadReference?.Trim(),
            ClaimType = request.ClaimType,
            ClaimDate = request.ClaimDate,
            ClaimValue = request.ClaimValue,
            Remarks = request.Remarks?.Trim(),
            Status = ClaimStatus.Open,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            CreatedBy = currentUser.DisplayName
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        claims.Add(claim);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(transporterId, [request.ClaimDate.ToDateTime(TimeOnly.MinValue)], cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterClaim", claim.Id.ToString(), "ClaimRecorded",
            NewValueJson: AuditJson.Serialize(new { claim.ClaimType, claim.ClaimDate, claim.ClaimValue, claim.LoadReference })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(claim);
    }

    public async Task<ClaimDto> ResolveAsync(long transporterId, long claimId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        var claim = await claims.FindAsync(claimId, cancellationToken);
        if (claim is null || claim.TransporterId != transporterId)
        {
            throw new NotFoundException($"Claim {claimId} was not found for transporter {transporterId}.", "CLAIM_NOT_FOUND");
        }

        if (claim.Status == ClaimStatus.Resolved)
        {
            throw new ConflictException($"Claim {claimId} is already resolved.", "CLAIM_ALREADY_RESOLVED");
        }

        claim.Status = ClaimStatus.Resolved;
        claim.ResolvedAt = clock.GetUtcNow().UtcDateTime;
        claim.ResolvedBy = currentUser.DisplayName;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(transporterId, [claim.ClaimDate.ToDateTime(TimeOnly.MinValue)], cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterClaim", claimId.ToString(), "ClaimResolved",
            NewValueJson: AuditJson.Serialize(new { claim.ResolvedAt })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(claim);
    }

    private static ClaimDto ToDto(TransporterClaim c) =>
        new(c.Id, c.TransporterId, c.LoadReference, c.ClaimType, c.ClaimDate, c.ClaimValue, c.Status, c.Remarks, c.ResolvedAt);
}

public sealed class RecordClaimRequestValidator : AbstractValidator<RecordClaimRequest>
{
    public RecordClaimRequestValidator()
    {
        RuleFor(x => x.ClaimValue).GreaterThanOrEqualTo(0).WithMessage("The claim value cannot be negative.");
        RuleFor(x => x.LoadReference).MaximumLength(60);
        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}
