using Fabolus.Core.Common;
using FluentAssertions;
using System;
using Xunit;

namespace Fabolus.Tests.Core;

public class ResultTests
{
    [Fact]
    public void Success_ReturnsSuccessResult()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        Action act = () => _ = result.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SuccessWithValue_ReturnsValue()
    {
        var result = Result.Success("test");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test");
    }

    [Fact]
    public void Failure_ReturnsFailureResult()
    {
        var error = new Error("TestError", "Description");
        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ToSuccessResult()
    {
        Result<string> result = "implicit";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("implicit");
    }

    [Fact]
    public void ImplicitConversion_FromError_ToFailureResult()
    {
        var error = new Error("Code", "Desc");
        Result<string> result = error;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FailureWithNone_ThrowsArgumentException()
    {
        Action act = () => Result.Failure(Error.None);

        act.Should().Throw<ArgumentException>();
    }
}
