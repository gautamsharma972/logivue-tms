namespace Tms.SharedKernel.Contracts;

/// <summary>Lets other modules (e.g. Platform, when linking a vendor-portal user) check a transporter exists in the caller's tenant.</summary>
public interface ITransporterDirectory
{
    Task<bool> ExistsAsync(Guid transporterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, TransporterInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
}
