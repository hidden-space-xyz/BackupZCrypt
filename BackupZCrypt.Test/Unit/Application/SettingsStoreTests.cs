using BackupZCrypt.Application.Settings;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the settings store, exercised with representative settings types: delegation to
/// the settings service, the absorption of load failures into the type's defaults, and the mapping
/// of save failures onto the result contract.
/// </summary>
public sealed class SettingsStoreTests
{
    /// <summary>
    /// The substituted settings service the store delegates to.
    /// </summary>
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();

    /// <summary>
    /// Creates a store over the substituted settings service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private SettingsStore CreateSut()
    {
        return new(this.settingsService);
    }

    [Fact]
    internal async Task LoadAsync_StoredSettings_ReturnsThemUnchanged()
    {
        RecentPathSettings stored = new("stored-source", "stored-destination");
        _ = this.settingsService
            .GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .Returns(stored);

        var result = await this.CreateSut()
            .LoadAsync<RecentPathSettings>(TestContext.Current.CancellationToken);

        Assert.Same(stored, result);
    }

    [Fact]
    internal async Task LoadAsync_LoadThrows_ReturnsTheDefaultsInsteadOfLeakingTheException()
    {
        _ = this.settingsService
            .GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk on fire"));

        var result = await this.CreateSut()
            .LoadAsync<RecentPathSettings>(TestContext.Current.CancellationToken);

        Assert.Equal(RecentPathSettings.DefaultValue, result);
    }

    [Fact]
    internal async Task LoadAsync_LoadCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.settingsService
            .GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut().LoadAsync<RecentPathSettings>(TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    internal async Task SaveAsync_SaveSucceeds_PersistsTheGivenInstanceAndReportsSuccess()
    {
        RecentPathSettings settings = new("some-source", "some-destination");

        var result = await this.CreateSut()
            .SaveAsync(settings, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        await this.settingsService.Received(1)
            .SaveAsync(settings, Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task SaveAsync_SaveThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        _ = this.settingsService
            .SaveAsync(Arg.Any<RecentPathSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));

        var result = await this.CreateSut()
            .SaveAsync(RecentPathSettings.DefaultValue, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Single(result.Errors),
            () => Assert.Equal(MessageCode.UnexpectedErrorFormat, result.Errors[0].Code),
            () => Assert.Equal<object>(["disk full"], result.Errors[0].Args)
        );
    }

    [Fact]
    internal async Task SaveAsync_SaveCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.settingsService
            .SaveAsync(Arg.Any<RecentPathSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .SaveAsync(RecentPathSettings.DefaultValue, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    internal void GetFilePath_AnySettingsType_ReturnsThePathTheSettingsServiceResolves()
    {
        _ = this.settingsService
            .GetFilePath<BackupCreationSettings>()
            .Returns("some-settings-path.json");

        var result = this.CreateSut().GetFilePath<BackupCreationSettings>();

        Assert.Equal("some-settings-path.json", result);
    }
}
