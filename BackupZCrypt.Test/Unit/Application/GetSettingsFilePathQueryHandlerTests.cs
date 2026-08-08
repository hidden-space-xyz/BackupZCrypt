using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Domain.Services.Interfaces;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the generic get-settings-file-path query handler, closed over one representative
/// settings type: synchronous delegation to the settings service.
/// </summary>
public sealed class GetSettingsFilePathQueryHandlerTests
{
    /// <summary>
    /// The substituted settings service the handler delegates to.
    /// </summary>
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();

    /// <summary>
    /// Creates a handler closed over <see cref="BackupCreationSettings"/>.
    /// </summary>
    /// <returns>The system under test.</returns>
    private GetSettingsFilePathQueryHandler<BackupCreationSettings> CreateSut()
    {
        return new(this.settingsService);
    }

    [Test]
    public void Handle_Query_ReturnsThePathTheSettingsServiceResolves()
    {
        _ = this.settingsService
            .GetFilePath<BackupCreationSettings>()
            .Returns("some-settings-path.json");

        var result = this.CreateSut().Handle(new GetSettingsFilePathQuery<BackupCreationSettings>());

        Assert.That(result, Is.EqualTo("some-settings-path.json"));
    }
}
