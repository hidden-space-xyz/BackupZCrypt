using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Domain.Services.Interfaces;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the generic get-settings query handler, closed over one representative settings
/// type: delegation to the settings service and the absorption of storage failures into the type's
/// defaults.
/// </summary>
public sealed class GetSettingsQueryHandlerTests
{
    /// <summary>
    /// The substituted settings service the handler delegates to.
    /// </summary>
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();

    /// <summary>
    /// Creates a handler closed over <see cref="RecentPathSettings"/>.
    /// </summary>
    /// <returns>The system under test.</returns>
    private GetSettingsQueryHandler<RecentPathSettings> CreateSut()
    {
        return new(this.settingsService);
    }

    [Fact]
    internal async Task HandleAsync_StoredSettings_ReturnsThemUnchanged()
    {
        RecentPathSettings stored = new("stored-source", "stored-destination");
        _ = this.settingsService
            .GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .Returns(stored);

        var result = await this.CreateSut()
            .HandleAsync(new GetSettingsQuery<RecentPathSettings>(), CancellationToken.None);

        Assert.Same(stored, result);
    }

    [Fact]
    internal async Task HandleAsync_LoadThrows_ReturnsTheDefaultsInsteadOfLeakingTheException()
    {
        _ = this.settingsService
            .GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk on fire"));

        var result = await this.CreateSut()
            .HandleAsync(new GetSettingsQuery<RecentPathSettings>(), CancellationToken.None);

        Assert.Equal(RecentPathSettings.DefaultValue, result);
    }
}
