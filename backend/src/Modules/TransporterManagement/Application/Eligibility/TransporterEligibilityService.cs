using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Eligibility;

/// <summary>Determines which transporters can take a load, with the reasons for every exclusion.</summary>
public interface ITransporterEligibilityService
{
    Task<IReadOnlyList<CandidateEvaluation>> CheckAsync(TransporterSelectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class TransporterEligibilityService(
    IEligibilityDataProvider provider,
    ITransporterSettings settings,
    IValidator<TransporterSelectionRequest> validator,
    TimeProvider clock,
    ILogger<TransporterEligibilityService> logger) : ITransporterEligibilityService
{
    public async Task<IReadOnlyList<CandidateEvaluation>> CheckAsync(TransporterSelectionRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var parameters = await BuildParametersAsync(request, cancellationToken);
        var data = await provider.LoadAsync(request, parameters.PerformanceWindowDays, cancellationToken);
        var results = TransporterEligibilityEvaluator.Evaluate(request, data, parameters);

        logger.LogInformation("Eligibility checked for lane {Origin}->{Destination} {ServiceType}: {Eligible} of {Candidates} eligible",
            request.OriginLocationId, request.DestinationLocationId, request.ServiceType, results.Count(r => r.Eligible), results.Count);

        return results;
    }

    internal async Task<EligibilityParameters> BuildParametersAsync(TransporterSelectionRequest request, CancellationToken cancellationToken)
    {
        var restrictions = await settings.GetAsync<EligibilityRestrictionsSetting>(SettingKeys.EligibilityRestrictions, cancellationToken);
        var reminder = await settings.GetAsync<int>(SettingKeys.ComplianceRenewalReminderDays, cancellationToken);
        var window = await settings.GetAsync<int>(SettingKeys.PerformanceWindowDays, cancellationToken);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSampleSize, cancellationToken);

        return new EligibilityParameters(
            RequestDate: request.RequiredDate,
            ComplianceDate: DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            DefaultReminderDays: reminder,
            PerformanceWindowDays: window,
            MinimumSampleSize: minimumSample,
            Restrictions: restrictions);
    }
}
