using Tms.SharedKernel.India;

namespace Tms.UnitTests.Transporters;

public class IndianIdentifiersTests
{
    [Theory]
    [InlineData("ABCDE1234F", true)]
    [InlineData("abcde1234f", true)] // normalised
    [InlineData("ABCDE12345", false)]
    [InlineData("ABCD1234F", false)]
    [InlineData("", false)]
    public void Pan(string pan, bool expected) => IndianIdentifiers.IsValidPan(pan).ShouldBe(expected);

    [Theory]
    [InlineData("27AAPFU0939F1ZV", true)] // well-known sample with a correct check character
    [InlineData("27aapfu0939f1zv", true)]
    [InlineData("27AAPFU0939F1ZW", false)] // wrong check character: a typo must be caught
    [InlineData("27AAPFU0939F1ZX", false)]
    [InlineData("27AAPFU0939F1Y5", false)] // 14th char must be Z
    [InlineData("27AAPFU0939F1Z", false)]
    public void Gstin(string gstin, bool expected) => IndianIdentifiers.IsValidGstin(gstin).ShouldBe(expected);

    [Fact]
    public void Gstin_EmbedsPanAndStateCode()
    {
        IndianIdentifiers.PanFromGstin("27AAPFU0939F1ZV").ShouldBe("AAPFU0939F");
        IndianIdentifiers.StateCodeFromGstin("27AAPFU0939F1ZV").ShouldBe("27");
    }

    [Theory]
    [InlineData("HDFC0001234", true)]
    [InlineData("hdfc0001234", true)]
    [InlineData("HDFC1001234", false)] // 5th char must be 0
    [InlineData("HDF0001234", false)]
    public void Ifsc(string ifsc, bool expected) => IndianIdentifiers.IsValidIfsc(ifsc).ShouldBe(expected);

    [Theory]
    [InlineData("98765 43210", "9876543210")]
    [InlineData("+91-98765-43210", "9876543210")]
    [InlineData("919876543210", "9876543210")]
    [InlineData("09876543210", "9876543210")]
    public void Mobile_IsNormalised(string input, string expected)
    {
        IndianIdentifiers.NormaliseMobile(input).ShouldBe(expected);
        IndianIdentifiers.IsValidMobile(input).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("98765")]
    public void Mobile_RejectsInvalid(string input) => IndianIdentifiers.IsValidMobile(input).ShouldBeFalse();

    [Theory]
    [InlineData("MH12AB1234", "MH12AB1234")]
    [InlineData("mh 12 ab 1234", "MH12AB1234")]
    [InlineData("DL-1C-AB-1234", "DL1CAB1234")]
    [InlineData("KA01A1234", "KA01A1234")]
    public void VehicleRegistration_Valid(string input, string normalised)
    {
        IndianIdentifiers.NormaliseVehicleRegistration(input).ShouldBe(normalised);
        IndianIdentifiers.IsValidVehicleRegistration(input).ShouldBeTrue();
    }

    [Theory]
    [InlineData("MH12AB12")]
    [InlineData("1234567890")]
    [InlineData("XYZ")]
    public void VehicleRegistration_Invalid(string input) => IndianIdentifiers.IsValidVehicleRegistration(input).ShouldBeFalse();

    [Theory]
    [InlineData("411001", true)]
    [InlineData("011001", false)]
    [InlineData("41100", false)]
    public void Pincode(string pincode, bool expected) => IndianIdentifiers.IsValidPincode(pincode).ShouldBe(expected);
}
