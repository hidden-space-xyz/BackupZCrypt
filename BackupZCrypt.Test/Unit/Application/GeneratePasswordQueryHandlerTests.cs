using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Domain.Enums;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the generate-password query handler: synchronous delegation of the length and
/// options to the password service.
/// </summary>
public sealed class GeneratePasswordQueryHandlerTests
{
    /// <summary>
    /// The substituted password service the handler delegates to.
    /// </summary>
    private readonly IPasswordService passwordService = Substitute.For<IPasswordService>();

    /// <summary>
    /// Creates a handler over the substituted password service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private GeneratePasswordQueryHandler CreateSut()
    {
        return new(this.passwordService);
    }

    [Test]
    public void Handle_Query_DelegatesLengthAndOptionsAndReturnsTheGeneratedPassword()
    {
        var options =
            PasswordGenerationOptions.IncludeUppercase | PasswordGenerationOptions.IncludeNumbers;
        _ = this.passwordService.GeneratePassword(50, options).Returns("generated-password");

        var result = this.CreateSut().Handle(new GeneratePasswordQuery(50, options));

        Assert.That(result, Is.EqualTo("generated-password"));
    }
}
