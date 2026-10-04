using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Eligibility;

/// <summary>Everything the eligibility rules need, loaded in bulk for one request.</summary>
public sealed record EligibilityData(
    IReadOnlyList<Transporter> Transporters,
    IReadOnlyList<TransporterLane> Lanes,
    IReadOnlyList<TransporterRate> Rates,
    IReadOnlyList<TransporterVehicle> Vehicles,
    IReadOnlyList<CapabilityHolding> Capabilities,
    IReadOnlyList<TransporterDocument> Documents,
    IReadOnlyList<DocumentType> DocumentTypes,
    IReadOnlyList<TransporterPlanningRule> PlanningRules,
    IReadOnlyList<PerformanceKpi> Kpis);

public sealed record CapabilityHolding(long TransporterId, string Code, DateTime EffectiveFrom, DateTime? EffectiveTo, RecordStatus Status);

/// <summary>Parameters that come from configuration or the request date.</summary>
public sealed record EligibilityParameters(
    DateOnly RequestDate,
    DateOnly ComplianceDate,
    int DefaultReminderDays,
    int PerformanceWindowDays,
    int MinimumSampleSize,
    EligibilityRestrictionsSetting Restrictions);

/// <summary>A KPI as used for scoring. <see cref="Value"/> is null unless the sample is sufficient.</summary>
public sealed record KpiPoint(decimal? Value, decimal Denominator, bool Sufficient, string Scope);

public sealed record RateQuote(long RateId, RateType RateType, decimal RateValue, decimal? EstimatedCost, string Currency, string? Note);

/// <summary>The outcome for one transporter. Reasons are always populated for ineligible transporters.</summary>
public sealed record CandidateEvaluation(
    long TransporterId,
    string Code,
    string Name,
    TransporterStatus Status,
    bool Eligible,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings,
    long? LaneId,
    bool Preferred,
    bool RestrictedForPlanning,
    RateQuote? Rate,
    int AvailableVehicles,
    ComplianceOverallStatus ComplianceStatus,
    IReadOnlyDictionary<KpiType, KpiPoint> Kpis);
