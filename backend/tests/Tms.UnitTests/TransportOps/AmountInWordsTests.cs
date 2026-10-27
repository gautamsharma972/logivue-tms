using Tms.Modules.TransportOps.Domain;

namespace Tms.UnitTests.TransportOps;

public class AmountInWordsTests
{
    [Theory]
    [InlineData(186808.11, "Indian Rupees One Lakh Eighty Six Thousand Eight Hundred Eight and Eleven Paise Only")]
    [InlineData(100, "Indian Rupees One Hundred Only")]
    [InlineData(118000, "Indian Rupees One Lakh Eighteen Thousand Only")]
    [InlineData(12345678.5, "Indian Rupees One Crore Twenty Three Lakh Forty Five Thousand Six Hundred Seventy Eight and Fifty Paise Only")]
    [InlineData(0.25, "Indian Rupees Twenty Five Paise Only")]
    [InlineData(0, "Indian Rupees Zero Only")]
    [InlineData(1000000, "Indian Rupees Ten Lakh Only")]
    [InlineData(21, "Indian Rupees Twenty One Only")]
    public void An_amount_is_written_the_Indian_way(double amount, string expected) =>
        AmountInWords.Rupees((decimal)amount).ShouldBe(expected);

    [Fact]
    public void Paise_are_rounded_to_two_places() => AmountInWords.Rupees(10.005m).ShouldBe("Indian Rupees Ten and One Paise Only");
}
