using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the result value objects.
/// </summary>
/// <remarks>
/// The implicit conversions are asserted alongside the factory method that each one forwards to, rather
/// than in methods of their own: a one-line forward cannot regress independently of its target, but the
/// conversion still has to be exercised so that <see cref="Result{T}"/>'s two implicit operators keep
/// resolving to the intended one. Both are in scope for a <see cref="MessageCode"/>, and binding one to
/// the value operator instead of the message operator would silently turn a failure into a success.
/// </remarks>
public sealed class ResultTests
{
    [Test]
    public void Failure_FromCodeAndArgsOrImplicitCode_CarriesSingleMatchingError()
    {
        var result = Result.Failure(MessageCode.PasswordTooShort, "extra", 42);
        Result implicitResult = MessageCode.InvalidPassword;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
            Assert.That(implicitResult.IsSuccess, Is.False);
            Assert.That(implicitResult.Errors, Has.Count.EqualTo(1));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Errors[0].Code, Is.EqualTo(MessageCode.PasswordTooShort));
            Assert.That(result.Errors[0].Args, Is.EqualTo(new object[] { "extra", 42 }));
            Assert.That(implicitResult.Errors[0].Code, Is.EqualTo(MessageCode.InvalidPassword));
            Assert.That(implicitResult.Errors[0].Args, Is.Empty);
        }
    }

    [Test]
    public void Failure_WithMessages_PreservesAllErrors()
    {
        var result = Result.Failure(
            new LocalizableMessage(MessageCode.SourcePathEmpty),
            new LocalizableMessage(MessageCode.PasswordRequired)
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Errors.Select(e => e.Code),
                Is.EqualTo([MessageCode.SourcePathEmpty, MessageCode.PasswordRequired])
            );
        }
    }

    [Test]
    public void GenericSuccess_FromFactoryOrImplicitValue_ExposesValueAndNoErrors()
    {
        var result = Result<int>.Success(7);
        Result<string> implicitResult = "ok";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(7));
            Assert.That(result.Errors, Is.Empty);
            Assert.That(implicitResult.IsSuccess, Is.True);
            Assert.That(implicitResult.Value, Is.EqualTo("ok"));
            Assert.That(implicitResult.Errors, Is.Empty);
        }
    }

    [Test]
    public void GenericFailure_FromFactoryOrImplicitCode_CarriesErrorAndBlocksValueAccess()
    {
        var result = Result<int>.Failure(MessageCode.UnexpectedErrorFormat, "boom");

        Result<string> implicitResult = MessageCode.InvalidPassword;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors.Select(e => e.Code), Is.EqualTo([MessageCode.UnexpectedErrorFormat]));
            Assert.That(implicitResult.IsSuccess, Is.False);
            Assert.That(implicitResult.Errors.Select(e => e.Code), Is.EqualTo([MessageCode.InvalidPassword]));
        }

        _ = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }
}
