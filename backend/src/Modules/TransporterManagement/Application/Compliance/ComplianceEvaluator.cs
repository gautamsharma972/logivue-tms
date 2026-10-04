using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Compliance;

/// <summary>
/// Pure compliance rules. Given the configured document types and a transporter's documents and vehicles
/// as of a date, it produces the report used for approval gates, eligibility blocks and alerts.
/// </summary>
public static class ComplianceEvaluator
{
    public static ComplianceReportDto Evaluate(
        long transporterId,
        DateOnly asOf,
        int defaultReminderDays,
        IReadOnlyList<DocumentType> documentTypes,
        IReadOnlyList<TransporterDocument> documents,
        IReadOnlyList<TransporterVehicle> vehicles,
        IReadOnlyList<TransporterDriver>? drivers = null)
    {
        var items = new List<ComplianceItemDto>();
        var activeTypes = documentTypes.Where(t => t.IsActive).ToList();

        foreach (var type in activeTypes.Where(t => t.IsTransporterLevel))
        {
            var latest = Latest(documents, d => d.VehicleId == null && d.DocumentTypeId == type.Id);
            if (latest is null && !type.IsMandatory)
            {
                continue;
            }

            items.Add(Assess(ComplianceScope.Transporter, null, type, latest, asOf, defaultReminderDays));
        }

        foreach (var vehicle in vehicles.Where(v => v.Status == RecordStatus.Active))
        {
            foreach (var type in activeTypes.Where(t => t.IsVehicleLevel))
            {
                var latest = Latest(documents, d => d.VehicleId == vehicle.Id && d.DocumentTypeId == type.Id);
                if (latest is null && !type.IsMandatory)
                {
                    continue;
                }

                items.Add(Assess(ComplianceScope.Vehicle, vehicle.Id, type, latest, asOf, defaultReminderDays));
            }
        }

        foreach (var driver in (drivers ?? []).Where(d => d.Status == RecordStatus.Active))
        {
            foreach (var type in activeTypes.Where(t => t.IsDriverLevel))
            {
                var latest = Latest(documents, d => d.DriverId == driver.Id && d.DocumentTypeId == type.Id);
                if (latest is null && !type.IsMandatory)
                {
                    continue;
                }

                items.Add(Assess(ComplianceScope.Driver, null, type, latest, asOf, defaultReminderDays, driver.Id));
            }
        }

        var approvalBlocked = items.Any(i => i.BlocksApproval);
        var blockedDrivers = items
            .Where(i => i.Scope == ComplianceScope.Driver && i.BlocksAllocation && i.DriverId is not null)
            .Select(i => i.DriverId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        var blockedVehicles = items
            .Where(i => i.Scope == ComplianceScope.Vehicle && i.BlocksAllocation && i.VehicleId is not null)
            .Select(i => i.VehicleId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        // A driver's expired licence stops that driver, not the whole transporter, so it does not block allocation.
        var allocationBlocked = approvalBlocked || items.Any(i => i.BlocksAllocation && i.Scope != ComplianceScope.Driver);

        var overall = approvalBlocked || blockedVehicles.Count > 0 || blockedDrivers.Count > 0
            ? ComplianceOverallStatus.NonCompliant
            : items.Any(i => i.State == ComplianceItemState.ExpiringSoon)
                ? ComplianceOverallStatus.ExpiringSoon
                : ComplianceOverallStatus.Compliant;

        return new ComplianceReportDto(transporterId, asOf, overall, approvalBlocked, allocationBlocked, blockedVehicles, items, blockedDrivers);
    }

    /// <summary>The most recent document is authoritative, so a later rejected upload overrides an older valid one.</summary>
    private static TransporterDocument? Latest(IEnumerable<TransporterDocument> documents, Func<TransporterDocument, bool> match) =>
        documents.Where(match).OrderByDescending(d => d.Id).FirstOrDefault();

    private static ComplianceItemDto Assess(
        ComplianceScope scope,
        long? vehicleId,
        DocumentType type,
        TransporterDocument? document,
        DateOnly asOf,
        int defaultReminderDays,
        long? driverId = null)
    {
        var isTransporterMandatory = scope == ComplianceScope.Transporter && type.IsMandatory;
        var reminderDays = type.RenewalReminderDays ?? defaultReminderDays;
        var scopeName = scope switch { ComplianceScope.Transporter => "transporter", ComplianceScope.Driver => "driver", _ => "vehicle" };

        if (document is null)
        {
            return Item(scope, vehicleId, null, type, ComplianceItemState.Missing, null,
                blocksApproval: isTransporterMandatory,
                blocksAllocation: type.IsMandatory,
                $"Mandatory {scopeName} document '{type.Name}' is missing.", driverId);
        }

        if (document.VerificationStatus == DocumentVerificationStatus.Rejected)
        {
            return Item(scope, vehicleId, document.Id, type, ComplianceItemState.Rejected, null,
                blocksApproval: isTransporterMandatory,
                blocksAllocation: type.IsMandatory,
                $"'{type.Name}' was rejected during verification.", driverId);
        }

        if (type.VerificationRequired && document.VerificationStatus != DocumentVerificationStatus.Verified)
        {
            return Item(scope, vehicleId, document.Id, type, ComplianceItemState.Unverified, null,
                blocksApproval: isTransporterMandatory,
                blocksAllocation: type.IsMandatory,
                $"'{type.Name}' has not been verified yet.", driverId);
        }

        if (type.ExpiryRequired && document.ExpiryDate is null)
        {
            return Item(scope, vehicleId, document.Id, type, ComplianceItemState.NoExpiryRecorded, null,
                blocksApproval: isTransporterMandatory,
                blocksAllocation: type.IsMandatory,
                $"'{type.Name}' has no expiry date recorded.", driverId);
        }

        if (document.ExpiryDate is { } expiry)
        {
            var days = expiry.DayNumber - asOf.DayNumber;

            if (days < 0)
            {
                return Item(scope, vehicleId, document.Id, type, ComplianceItemState.Expired, days,
                    blocksApproval: isTransporterMandatory,
                    blocksAllocation: isTransporterMandatory || type.BlockAllocationWhenExpired,
                    $"'{type.Name}' expired on {expiry:yyyy-MM-dd}.", driverId);
            }

            if (days <= reminderDays)
            {
                return Item(scope, vehicleId, document.Id, type, ComplianceItemState.ExpiringSoon, days,
                    blocksApproval: false,
                    blocksAllocation: false,
                    $"'{type.Name}' expires on {expiry:yyyy-MM-dd} ({days} days).", driverId);
            }
        }

        return Item(scope, vehicleId, document.Id, type, ComplianceItemState.Valid,
            document.ExpiryDate is { } e ? e.DayNumber - asOf.DayNumber : null,
            blocksApproval: false, blocksAllocation: false, $"'{type.Name}' is valid.", driverId);
    }

    private static ComplianceItemDto Item(
        ComplianceScope scope,
        long? vehicleId,
        long? documentId,
        DocumentType type,
        ComplianceItemState state,
        int? daysToExpiry,
        bool blocksApproval,
        bool blocksAllocation,
        string message,
        long? driverId = null) =>
        new(scope, vehicleId, documentId, type.Code, type.Name, state, daysToExpiry, blocksApproval, blocksAllocation, message, driverId);
}
