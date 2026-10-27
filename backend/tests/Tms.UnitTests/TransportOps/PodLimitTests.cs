using Tms.Modules.TransportOps.Domain;

namespace Tms.UnitTests.TransportOps;

public class PodLimitTests
{
    [Theory]
    [InlineData(20, 10)]
    [InlineData(10, 10)]
    [InlineData(4, 4)]
    [InlineData(0, 1)]
    public void A_proof_never_carries_more_than_ten_files_whatever_an_older_setting_says(int configured, int expected) =>
        new PodSetting(configured).EffectiveMaxFiles.ShouldBe(expected);
}
