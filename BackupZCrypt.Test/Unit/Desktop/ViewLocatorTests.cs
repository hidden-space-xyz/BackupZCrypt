using Avalonia.Controls;

using BackupZCrypt.Desktop;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// UI tests for <see cref="ViewLocator"/>: every page ViewModel finds its view by name, and anything else is
/// either not matched or shown as a visible placeholder instead of failing.
/// </summary>
public sealed class ViewLocatorTests
{
    [Fact]
    internal Task Build_EveryPageOfTheShell_CreatesItsView()
    {
        return HeadlessUi.RunAsync(() =>
        {
            using var services = DesktopShell.CreateServices();
            var shell = services.GetRequiredService<MainWindowViewModel>();
            ViewLocator sut = new();

            var views = shell.OperationItems.Concat(shell.UtilityItems)
                .Select(item => sut.Build(item.Page)?.GetType())
                .ToArray();

            Assert.Equal(
                [
                    typeof(CreateBackupView),
                    typeof(UpdateBackupView),
                    typeof(RestoreBackupView),
                    typeof(VerifyBackupView),
                    typeof(SettingsView),
                    typeof(AboutView),
                ],
                views
            );
        });
    }

    [Fact]
    internal Task Build_WithoutAMatchingView_ShowsWhatWasNotFound()
    {
        return HeadlessUi.RunAsync(() =>
        {
            using var services = DesktopShell.CreateServices();
            ViewLocator sut = new();

            var placeholder = Assert.IsType<TextBlock>(sut.Build(services.GetRequiredService<MainWindowViewModel>()));

            Assert.Multiple(
                () => Assert.Equal("Not Found: BackupZCrypt.Desktop.Views.MainWindowView", placeholder.Text),
                () => Assert.Null(sut.Build(null))
            );
        });
    }

    [Fact]
    internal void Match_AcceptsOnlyViewModels()
    {
        ViewLocator sut = new();

        Assert.Multiple(
            () => Assert.True(sut.Match(new OperationProbe())),
            () => Assert.False(sut.Match("text")),
            () => Assert.False(sut.Match(null))
        );
    }

    /// <summary>
    /// A ViewModel with no view of its own.
    /// </summary>
    private sealed class OperationProbe : ViewModelBase;
}
