using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Abstractions;

/// <summary>
/// Read-side access for tenders. A non-null <c>scopeTransporterId</c> restricts results to that transporter;
/// the vendor portal always passes it, so no vendor can read another vendor's invitations.
/// </summary>
public interface ITenderQueries
{
    Task<PagedResult<TenderInvitationDto>> SearchAsync(TenderSearch search, CancellationToken cancellationToken = default);

    Task<TenderDetailDto?> GetDetailAsync(long invitationId, long? scopeTransporterId, CancellationToken cancellationToken = default);

    Task<PagedResult<TenderInvitationDto>> VendorInvitationsAsync(long transporterId, TenderStatus? status, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<VendorDashboardDto> VendorDashboardAsync(long transporterId, DateOnly today, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VendorLoadDto>> VendorLoadsAsync(long transporterId, CancellationToken cancellationToken = default);
}
