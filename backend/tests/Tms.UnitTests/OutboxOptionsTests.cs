using Tms.SharedKernel.Persistence;

namespace Tms.UnitTests;

public class OutboxOptionsTests
{
    [Fact]
    public void RetryDelay_GrowsExponentiallyAndIsCapped()
    {
        var options = new OutboxOptions { FirstRetrySeconds = 30 };

        options.RetryDelay(1).ShouldBe(TimeSpan.FromSeconds(30));
        options.RetryDelay(2).ShouldBe(TimeSpan.FromMinutes(2));
        options.RetryDelay(3).ShouldBe(TimeSpan.FromMinutes(8));
        options.RetryDelay(100).ShouldBe(options.RetryDelay(7), "the backoff is capped");
    }
}
