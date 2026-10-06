using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Tracking.Integration;

internal sealed class TrackingDemoSeeder : ITrackingDemoSeeder
{
    public Task<DemoSeedResult> SeedAsync(CancellationToken cancellationToken) => Task.FromResult(new DemoSeedResult(0, 0, 0, 0, "Not yet built."));
}
