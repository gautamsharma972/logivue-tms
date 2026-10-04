using LogiVue.Tms.TransporterManagement.Application.Integration;

namespace LogiVue.Tms.TransporterManagement.Application.Eligibility;

/// <summary>Loads the data the eligibility rules need for one request, in bulk.</summary>
public interface IEligibilityDataProvider
{
    Task<EligibilityData> LoadAsync(TransporterSelectionRequest request, int performanceWindowDays, CancellationToken cancellationToken = default);
}
