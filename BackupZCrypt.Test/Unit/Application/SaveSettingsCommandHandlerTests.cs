using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the generic save-settings command handler, closed over one representative settings
/// type: delegation to the settings service and the mapping of storage failures onto the result
/// contract.
/// </summary>
public sealed class SaveSettingsCommandHandlerTests
{
    /// <summary>
    /// The substituted settings service the handler delegates to.
    /// </summary>
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();

    /// <summary>
    /// Creates a handler closed over <see cref="RecentPathSettings"/>.
    /// </summary>
    /// <returns>The system under test.</returns>
    private SaveSettingsCommandHandler<RecentPathSettings> CreateSut()
    {
        return new(this.settingsService);
    }

    [Fact]
    internal async Task HandleAsync_SaveSucceeds_PersistsTheGivenInstanceAndReportsSuccess()
    {
        RecentPathSettings settings = new("some-source", "some-destination");

        var result = await this.CreateSut()
            .HandleAsync(new SaveSettingsCommand<RecentPathSettings>(settings), CancellationToken.None);

        Assert.True(result.IsSuccess);

        await this.settingsService.Received(1)
            .SaveAsync(settings, Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task HandleAsync_SaveThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        _ = this.settingsService
            .SaveAsync(Arg.Any<RecentPathSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));

        var result = await this.CreateSut()
            .HandleAsync(
                new SaveSettingsCommand<RecentPathSettings>(RecentPathSettings.DefaultValue),
                CancellationToken.None
            );

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Single(result.Errors),
            () => Assert.Equal(MessageCode.UnexpectedErrorFormat, result.Errors[0].Code),
            () => Assert.Equal<object>(["disk full"], result.Errors[0].Args)
        );
    }
}
