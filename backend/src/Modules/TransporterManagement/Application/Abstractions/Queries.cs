using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Alerts;
using LogiVue.Tms.TransporterManagement.Application.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Abstractions;

/// <summary>
/// Read-side access. Returns projected DTOs so list screens never load entities or trigger N+1 queries.
/// </summary>
public interface ITransporterQueries
{
    Task<PagedResult<TransporterListItem>> SearchTransportersAsync(TransporterSearch search, CancellationToken cancellationToken = default);

    Task<TransporterDetail?> GetTransporterAsync(long id, CancellationToken cancellationToken = default);

    Task<PagedResult<VehicleDto>> SearchVehiclesAsync(long transporterId, VehicleSearch search, CancellationToken cancellationToken = default);

    Task<PagedResult<LaneDto>> SearchLanesAsync(long transporterId, LaneSearch search, CancellationToken cancellationToken = default);

    Task<PagedResult<AlertDto>> SearchAlertsAsync(AlertSearch search, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetTransporterTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetCapabilityTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetServiceTypesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LookupDto>> GetDocumentTypesAsync(CancellationToken cancellationToken = default);
}
