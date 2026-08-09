using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the analyze-password-strength query handler: synchronous delegation to the
/// password service and the redaction of the password in the query's text form.
/// </summary>
public sealed class AnalyzePasswordStrengthQueryHandlerTests
{
    /// <summary>
    /// The substituted password service the handler delegates to.
    /// </summary>
    private readonly IPasswordService passwordService = Substitute.For<IPasswordService>();

    /// <summary>
    /// Creates a handler over the substituted password service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private AnalyzePasswordStrengthQueryHandler CreateSut()
    {
        return new(this.passwordService);
    }

    [Fact]
    internal void Handle_Query_DelegatesThePasswordAndReturnsTheAnalysisUnchanged()
    {
        PasswordStrengthAnalysis analysis = new(PasswordStrength.Strong, 87.5, 120.0, []);
        _ = this.passwordService.AnalyzePasswordStrength("candidate-password").Returns(analysis);

        var result = this.CreateSut().Handle(new AnalyzePasswordStrengthQuery("candidate-password"));

        Assert.Same(analysis, result);
    }

    [Fact]
    internal void ToString_OfTheQuery_RedactsThePassword()
    {
        var query = new AnalyzePasswordStrengthQuery("hunter2-secret");

        var text = query.ToString();

        Assert.Multiple(
            () => Assert.DoesNotContain("hunter2-secret", text, StringComparison.Ordinal),
            () => Assert.Contains("***", text, StringComparison.Ordinal)
        );
    }
}
