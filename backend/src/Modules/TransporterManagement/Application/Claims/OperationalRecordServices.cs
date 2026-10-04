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

public sealed record RecordLoadCostRequest(string LoadReference, DateOnly ServiceDate, decimal AgreedAmount, decimal InvoicedAmount);

public sealed record LoadCostDto(long Id, long TransporterId, string LoadReference, DateOnly ServiceDate, decimal AgreedAmount, decimal InvoicedAmount, bool OnBudget);

/// <summary>Capacity for one day. <see cref="VehiclesAvailable"/> never exceeds <see cref="VehiclesCommitted"/>.</summary>
public sealed record CapacityDayDto(long Id, long TransporterId, DateOnly Date, int VehiclesCommitted, int VehiclesAvailable, DateTime UpdatedAt);

public sealed record SaveCapacityRequest(DateOnly Date, int VehiclesCommitted, int VehiclesAvailable);

public interface ILoadCostService
{
    Task<LoadCostDto> RecordAsync(long transporterId, RecordLoadCostRequest request, CancellationToken cancellationToken = default);
}

public interface ICapacityService
{
    Task<IReadOnlyList<CapacityDayDto>> ListAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<CapacityDayDto> SaveAsync(long transporterId, SaveCapacityRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Invoiced cost per load. One record per load, so a load cannot be counted twice in cost performance.</summary>
public sealed class LoadCostService(
    IRepository<Transporter> transporters,
    IRepository<LoadCost> costs,
    IPerformanceService performance,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<RecordLoadCostRequest> validator) : ILoadCostService
{
    public async Task<LoadCostDto> RecordAsync(long transporterId, RecordLoadCostRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);

        var load = request.LoadReference.Trim();
        if (await costs.AnyAsync(c => c.TransporterId == transporterId && c.LoadReference == load, cancellationToken))
        {
            throw new ConflictException($"A cost is already recorded for load {load}.", "LOAD_COST_DUPLICATE");
        }

        var cost = new LoadCost
        {
            TransporterId = transporterId,
            LoadReference = load,
            ServiceDate = request.ServiceDate,
            AgreedAmount = request.AgreedAmount,
            InvoicedAmount = request.InvoicedAmount,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            CreatedBy = currentUser.DisplayName
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        costs.Add(cost);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(transporterId, [request.ServiceDate.ToDateTime(TimeOnly.MinValue)], cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterLoadCost", cost.Id.ToString(), "LoadCostRecorded",
            NewValueJson: AuditJson.Serialize(new { load, request.AgreedAmount, request.InvoicedAmount })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new LoadCostDto(cost.Id, transporterId, load, cost.ServiceDate, cost.AgreedAmount, cost.InvoicedAmount,
            cost.InvoicedAmount <= cost.AgreedAmount);
    }
}

/// <summary>
/// Daily vehicle capacity. Vendors report it through the portal; saving a day replaces the earlier figures for that day.
/// </summary>
public sealed class CapacityService(
    IRepository<Transporter> transporters,
    IRepository<CapacityDay> days,
    IPerformanceService performance,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    IValidator<SaveCapacityRequest> validator) : ICapacityService
{
    public async Task<IReadOnlyList<CapacityDayDto>> ListAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        return (await days.ListAsync(d => d.TransporterId == transporterId && d.Date >= from && d.Date <= to, cancellationToken))
            .OrderBy(d => d.Date).Select(ToDto).ToList();
    }

    public async Task<CapacityDayDto> SaveAsync(long transporterId, SaveCapacityRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        var transporter = await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        TransporterGuards.EnsureEditable(transporter);

        var now = clock.GetUtcNow().UtcDateTime;
        var day = (await days.ListAsync(d => d.TransporterId == transporterId && d.Date == request.Date, cancellationToken)).SingleOrDefault();
        var before = day is null ? null : AuditJson.Serialize(new { day.VehiclesCommitted, day.VehiclesAvailable });
        if (day is null)
        {
            day = new CapacityDay { TransporterId = transporterId, Date = request.Date };
            days.Add(day);
        }

        day.VehiclesCommitted = request.VehiclesCommitted;
        day.VehiclesAvailable = request.VehiclesAvailable;
        day.UpdatedAt = now;
        day.UpdatedBy = currentUser.DisplayName;

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await performance.RefreshAsync(transporterId, [request.Date.ToDateTime(TimeOnly.MinValue)], cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterCapacity", day.Id.ToString(), "CapacitySaved",
            OldValueJson: before, NewValueJson: AuditJson.Serialize(new { day.VehiclesCommitted, day.VehiclesAvailable })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(day);
    }

    private static CapacityDayDto ToDto(CapacityDay d) =>
        new(d.Id, d.TransporterId, d.Date, d.VehiclesCommitted, d.VehiclesAvailable, d.UpdatedAt);
}

public sealed class RecordLoadCostRequestValidator : AbstractValidator<RecordLoadCostRequest>
{
    public RecordLoadCostRequestValidator()
    {
        RuleFor(x => x.LoadReference).NotEmpty().MaximumLength(60);
        RuleFor(x => x.AgreedAmount).GreaterThan(0).WithMessage("The agreed amount must be greater than zero.");
        RuleFor(x => x.InvoicedAmount).GreaterThanOrEqualTo(0).WithMessage("The invoiced amount cannot be negative.");
    }
}

public sealed class SaveCapacityRequestValidator : AbstractValidator<SaveCapacityRequest>
{
    public SaveCapacityRequestValidator()
    {
        RuleFor(x => x.VehiclesCommitted).InclusiveBetween(0, 10000);
        RuleFor(x => x.VehiclesAvailable).InclusiveBetween(0, 10000)
            .LessThanOrEqualTo(x => x.VehiclesCommitted).WithMessage("Available vehicles cannot exceed committed vehicles.");
    }
}
