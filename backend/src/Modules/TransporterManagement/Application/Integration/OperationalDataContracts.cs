namespace LogiVue.Tms.TransporterManagement.Application.Integration;

/// <summary>Cost performance inputs for one transporter and period. Owned by the cost ledger; read-only here.</summary>
/// <param name="LoadsWithCost">Loads with an invoiced amount in the period. The denominator of cost performance.</param>
/// <param name="OnBudgetLoads">Loads invoiced at or below the agreed amount. The numerator of cost performance.</param>
public record TransporterCostSummaryDto(
    long TransporterId,
    int LoadsWithCost,
    int OnBudgetLoads,
    decimal AgreedAmount,
    decimal InvoicedAmount);

/// <summary>Availability inputs for one transporter and period. Summed vehicle-days, so the ratio weights busy days correctly.</summary>
public record TransporterAvailabilitySummaryDto(
    long TransporterId,
    int VehicleDaysCommitted,
    int VehicleDaysAvailable);

/// <summary>Cost data source. Implemented locally from the cost ledger until the finance feed exists.</summary>
public interface ITransporterCostProvider
{
    Task<TransporterCostSummaryDto> GetCostSummaryAsync(long transporterId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}

/// <summary>Availability data source. Implemented locally from vendor-reported capacity until fleet telemetry exists.</summary>
public interface ITransporterAvailabilityProvider
{
    Task<TransporterAvailabilitySummaryDto> GetAvailabilitySummaryAsync(long transporterId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
