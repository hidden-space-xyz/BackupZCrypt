using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the result value objects.
/// </summary>
public sealed class ResultTests
{
    [Test]
    public void Failure_WithCodeAndArgs_CarriesSingleErrorWithCodeAndArgs()
    {
        var result = Result.Failure(MessageCode.PasswordTooShort, "extra", 42);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
        }
        var error = result.Errors[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Code, Is.EqualTo(MessageCode.PasswordTooShort));
            Assert.That(error.Args, Is.EqualTo(new object[] { "extra", 42 }));
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
    public void ImplicitMessageCode_ConvertsToFailureResult()
    {
        Result result = MessageCode.InvalidPassword;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
        }
        Assert.That(result.Errors[0].Code, Is.EqualTo(MessageCode.InvalidPassword));
    }

    [Test]
    public void GenericSuccess_ExposesValueAndNoErrors()
    {
        var result = Result<int>.Success(7);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(7));
            Assert.That(result.Errors, Is.Empty);
        }
    }

    [Test]
    public void GenericImplicitValue_ConvertsToSuccess()
    {
        Result<string> result = "ok";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo("ok"));
        }
    }

    [Test]
    public void GenericImplicitMessageCode_ConvertsToFailure()
    {
        Result<string> result = MessageCode.InvalidPassword;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Has.Count.EqualTo(1));
        }
        Assert.That(result.Errors[0].Code, Is.EqualTo(MessageCode.InvalidPassword));
    }

    [Test]
    public void GenericFailure_AccessingValue_Throws()
    {
        var result = Result<int>.Failure(MessageCode.UnexpectedErrorFormat, "boom");

        Assert.That(result.IsSuccess, Is.False);
        _ = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }
}
