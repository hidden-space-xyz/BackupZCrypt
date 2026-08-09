using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the result value objects.
/// </summary>
/// <remarks>
/// The implicit conversions are asserted alongside the factory method that each one forwards to, rather
/// than in methods of their own: a one-line forward cannot regress independently of its target, but the
/// conversion still has to be exercised so that the two implicit operators of
/// <see cref="BackupZCrypt.Application.ValueObjects.Result{T}"/> keep resolving to the intended one. Both are
/// in scope for a <see cref="MessageCode"/>, and binding one to the value operator instead of the message
/// operator would silently turn a failure into a success.
/// </remarks>
public sealed class ResultTests
{
    [Fact]
    internal void Failure_FromCodeAndArgsOrImplicitCode_CarriesSingleMatchingError()
    {
        var result = Result.Failure(MessageCode.PasswordTooShort, "extra", 42);
        Result implicitResult = MessageCode.InvalidPassword;

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Single(result.Errors),
            () => Assert.False(implicitResult.IsSuccess),
            () => Assert.Single(implicitResult.Errors)
        );

        object[] expectedArgs = ["extra", 42];

        Assert.Multiple(
            () => Assert.Equal(MessageCode.PasswordTooShort, result.Errors[0].Code),
            () => Assert.Equal(expectedArgs, result.Errors[0].Args),
            () => Assert.Equal(MessageCode.InvalidPassword, implicitResult.Errors[0].Code),
            () => Assert.Empty(implicitResult.Errors[0].Args)
        );
    }

    [Fact]
    internal void Failure_WithMessages_PreservesAllErrors()
    {
        var result = Result.Failure(
            new LocalizableMessage(MessageCode.SourcePathEmpty),
            new LocalizableMessage(MessageCode.PasswordRequired)
        );

        MessageCode[] expectedCodes = [MessageCode.SourcePathEmpty, MessageCode.PasswordRequired];

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal(expectedCodes, result.Errors.Select(e => e.Code))
        );
    }

    [Fact]
    internal void GenericSuccess_FromFactoryOrImplicitValue_ExposesValueAndNoErrors()
    {
        var result = Result<int>.Success(7);
        Result<string> implicitResult = "ok";

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.Equal(7, result.Value),
            () => Assert.Empty(result.Errors),
            () => Assert.True(implicitResult.IsSuccess),
            () => Assert.Equal("ok", implicitResult.Value),
            () => Assert.Empty(implicitResult.Errors)
        );
    }

    [Fact]
    internal void GenericFailure_FromFactoryOrImplicitCode_CarriesErrorAndBlocksValueAccess()
    {
        var result = Result<int>.Failure(MessageCode.UnexpectedErrorFormat, "boom");

        Result<string> implicitResult = MessageCode.InvalidPassword;

        MessageCode[] expectedCodes = [MessageCode.UnexpectedErrorFormat];
        MessageCode[] expectedImplicitCodes = [MessageCode.InvalidPassword];

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal(expectedCodes, result.Errors.Select(e => e.Code)),
            () => Assert.False(implicitResult.IsSuccess),
            () => Assert.Equal(expectedImplicitCodes, implicitResult.Errors.Select(e => e.Code))
        );

        _ = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }
}
