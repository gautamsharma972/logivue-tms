using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;

namespace LogiVue.Tms.TransporterManagement.Application.Common;

/// <summary>Lanes and tenders may only use active service types from the catalogue.</summary>
public static class ServiceTypeGuard
{
    public static async Task EnsureActiveAsync(IRepository<ServiceTypeDefinition> serviceTypes, string code, CancellationToken cancellationToken)
    {
        var normalised = code.Trim().ToUpperInvariant();
        if (!await serviceTypes.AnyAsync(s => s.Code == normalised && s.IsActive, cancellationToken))
        {
            throw new BusinessRuleException($"'{normalised}' is not an active service type.", "SERVICE_TYPE_INVALID");
        }
    }
}
