namespace Tms.SharedKernel.Contracts;

/// <summary>Read-only lookup of an organisation's short code for modules that must not reference Platform (implemented by Platform).</summary>
public interface ITenantDirectory
{
    /// <summary>The organisation's code (e.g. <c>DEMO</c>), or null when unknown.</summary>
    Task<string?> GetCodeAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>The business modules the organisation has; null means all of them.</summary>
    Task<IReadOnlySet<string>?> GetModulesAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
