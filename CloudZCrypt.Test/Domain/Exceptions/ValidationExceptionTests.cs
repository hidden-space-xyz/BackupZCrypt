using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;

namespace CloudZCrypt.Test.Domain.Exceptions;

[TestFixture]
internal sealed class ValidationExceptionTests
{
    [Test]
    public void Constructor_SetsCodeAndMessage()
    {
        ValidationException ex = new(
            ValidationErrorCode.PasswordLengthNonPositive,
            "Length must be positive",
            "length"
        );

        Assert.That(ex.Code, Is.EqualTo(ValidationErrorCode.PasswordLengthNonPositive));
        Assert.That(ex.Message, Is.EqualTo("Length must be positive"));
        Assert.That(ex.ParameterName, Is.EqualTo("length"));
    }

    [Test]
    public void Constructor_NullMessage_UsesCodeAsMessage()
    {
        ValidationException ex = new(ValidationErrorCode.Unknown);

        Assert.That(ex.Message, Is.EqualTo(nameof(ValidationErrorCode.Unknown)));
        Assert.That(ex.ParameterName, Is.Null);
    }
}
