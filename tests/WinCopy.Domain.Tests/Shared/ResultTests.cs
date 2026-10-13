using Shouldly;
using WinCopy.Domain.Shared;
using Xunit;

#pragma warning disable CA1716 // El espacio de pruebas refleja la carpeta Shared de la especificación.
namespace WinCopy.Domain.Tests.Shared;
#pragma warning restore CA1716

public sealed class ResultTests
{
    [Fact]
    public void SuccessIsSuccessAndExposesTheValue()
    {
        var result = Result<int, DomainError>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void SuccessHasNoError()
    {
        var result = Result<int, DomainError>.Success(42);

        Should.Throw<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void FailureIsNotSuccessAndExposesTheError()
    {
        var error = new DomainError("NoSystemDisk", "Sin disco del sistema");
        var result = Result<int, DomainError>.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void FailureHasNoValue()
    {
        var result = Result<int, DomainError>.Failure(new DomainError("Code"));

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void FailureRejectsANullError()
    {
        Should.Throw<ArgumentNullException>(() => Result<int, DomainError>.Failure(null!));
    }

    [Fact]
    public void MatchRunsTheSuccessBranchForASuccess()
    {
        var result = Result<int, DomainError>.Success(42);

        var text = result.Match(value => $"ok {value}", error => $"error {error.Code}");

        text.ShouldBe("ok 42");
    }

    [Fact]
    public void MatchRunsTheFailureBranchForAFailure()
    {
        var result = Result<int, DomainError>.Failure(new DomainError("TargetInUse"));

        var text = result.Match(value => $"ok {value}", error => $"error {error.Code}");

        text.ShouldBe("error TargetInUse");
    }

    [Fact]
    public void DomainErrorDetailDefaultsToNull()
    {
        new DomainError("Code").Detail.ShouldBeNull();
    }

    [Fact]
    public void DomainErrorHasValueEquality()
    {
        new DomainError("Code", "detail").ShouldBe(new DomainError("Code", "detail"));
        new DomainError("Code").ShouldNotBe(new DomainError("Other"));
    }
}
