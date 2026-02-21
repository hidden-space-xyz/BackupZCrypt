namespace BackupZCrypt.Test.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;

[TestFixture]
internal sealed class ValidationExceptionTests
{
    [Test]
    public void Constructor_SetsCodeAndMessage()
    {
        ValidationException ex = new(
            ValidationErrorCode.PasswordLengthNonPositive,
            "Length must be positive",
            "length");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(ValidationErrorCode.PasswordLengthNonPositive));
            Assert.That(ex.Message, Is.EqualTo("Length must be positive"));
            Assert.That(ex.ParameterName, Is.EqualTo("length"));
        }
    }

    [Test]
    public void Constructor_NullMessage_UsesCodeAsMessage()
    {
        ValidationException ex = new(ValidationErrorCode.Unknown);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Is.EqualTo(nameof(ValidationErrorCode.Unknown)));
            Assert.That(ex.ParameterName, Is.Null);
        }
    }
}
