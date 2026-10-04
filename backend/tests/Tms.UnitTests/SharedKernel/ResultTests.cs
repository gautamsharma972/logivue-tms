using Tms.SharedKernel.Results;

namespace Tms.UnitTests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_CarriesError_AndRefusesToExposeValue()
    {
        Result<int> result = Error.NotFound("x.not_found", "missing");

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void NonGenericResult_ConvertsImplicitlyFromError()
    {
        Result result = Error.Conflict("x.conflict", "nope");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("x.conflict");
    }
}
