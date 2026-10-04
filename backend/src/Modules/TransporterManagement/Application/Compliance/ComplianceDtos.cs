namespace LogiVue.Tms.TransporterManagement.Application.Compliance;

public enum ComplianceOverallStatus { Compliant, ExpiringSoon, NonCompliant }

public enum ComplianceItemState { Missing, Unverified, Rejected, NoExpiryRecorded, Expired, ExpiringSoon, Valid }

public enum ComplianceScope { Transporter, Vehicle, Driver }

public sealed record ComplianceItemDto(
    ComplianceScope Scope,
    long? VehicleId,
    long? DocumentId,
    string DocumentTypeCode,
    string DocumentTypeName,
    ComplianceItemState State,
    int? DaysToExpiry,
    bool BlocksApproval,
    bool BlocksAllocation,
    string Message,
    long? DriverId = null);

public sealed record ComplianceReportDto(
    long TransporterId,
    DateOnly AsOf,
    ComplianceOverallStatus Overall,
    bool ApprovalBlocked,
    bool AllocationBlocked,
    IReadOnlyList<long> BlockedVehicleIds,
    IReadOnlyList<ComplianceItemDto> Items,
    IReadOnlyList<long>? BlockedDriverIds = null);

public sealed record ComplianceEvaluationResult(int AlertsRaised, int AlertsResolved, int TransportersEvaluated);
