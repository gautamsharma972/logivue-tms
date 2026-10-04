using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Transporters;

/// <summary>Shared lookups and state checks used by the transporter, fleet and coverage services.</summary>
internal static class TransporterGuards
{
    public static async Task<Transporter> LoadAsync(IRepository<Transporter> transporters, long id, CancellationToken ct) =>
        await transporters.FindAsync(id, ct)
        ?? throw new NotFoundException($"Transporter {id} was not found.", "TRANSPORTER_NOT_FOUND");

    /// <summary>Blacklisted transporters are read-only: no master, contact, fleet, lane or capability changes.</summary>
    public static void EnsureEditable(Transporter transporter)
    {
        if (transporter.Status == TransporterStatus.Blacklisted)
        {
            throw new BusinessRuleException(
                $"Transporter {transporter.TransporterCode} is blacklisted and cannot be changed.", "TRANSPORTER_LOCKED");
        }
    }

    public static async Task EnsureActiveTransporterTypeAsync(
        IRepository<TransporterType> types, long? transporterTypeId, CancellationToken ct)
    {
        if (transporterTypeId is null)
        {
            return;
        }

        var type = await types.FindAsync(transporterTypeId.Value, ct);
        if (type is null || !type.IsActive)
        {
            throw new BusinessRuleException(
                $"Transporter type {transporterTypeId} is not available.", "TRANSPORTER_TYPE_INVALID");
        }
    }
}
