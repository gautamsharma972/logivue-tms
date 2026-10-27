namespace Tms.SharedKernel.Contracts;

/// <summary>What an organisation is allowed to have. Null means no limit. Set by the platform operator.</summary>
public sealed record TenantLimits(int? MaxUsers, int? MaxLocations);

/// <summary>Read-only lookup of an organisation's limits, for modules that enforce one (implemented by Platform).</summary>
public interface ITenantLimits
{
    Task<TenantLimits> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
