using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

public sealed class ResultTests
{
    [Fact]
    public void Failure_WithCodeAndArgs_CarriesSingleErrorWithCodeAndArgs()
    {
        var result = Result.Failure(MessageCode.PasswordTooShort, "extra", 42);

        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal(MessageCode.PasswordTooShort, error.Code);
        Assert.Equal(new object[] { "extra", 42 }, error.Args);
    }

    [Fact]
    public void Failure_WithMessages_PreservesAllErrors()
    {
        var result = Result.Failure(
            new LocalizableMessage(MessageCode.SourcePathEmpty),
            new LocalizableMessage(MessageCode.PasswordRequired)
        );

        Assert.False(result.IsSuccess);
        Assert.Equal(
            new[] { MessageCode.SourcePathEmpty, MessageCode.PasswordRequired },
            result.Errors.Select(e => e.Code)
        );
    }

    [Fact]
    public void ImplicitMessageCode_ConvertsToFailureResult()
    {
        Result result = MessageCode.InvalidPassword;

        Assert.False(result.IsSuccess);
        Assert.Equal(MessageCode.InvalidPassword, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void GenericSuccess_ExposesValueAndNoErrors()
    {
        var result = Result<int>.Success(7);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void GenericImplicitValue_ConvertsToSuccess()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void GenericImplicitMessageCode_ConvertsToFailure()
    {
        Result<string> result = MessageCode.InvalidPassword;

        Assert.False(result.IsSuccess);
        Assert.Equal(MessageCode.InvalidPassword, Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        var result = Result<int>.Failure(MessageCode.UnexpectedErrorFormat, "boom");

        Assert.False(result.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
