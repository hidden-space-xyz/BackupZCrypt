using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Desktop.ViewModels;

/// <summary>
/// Unit tests for <see cref="MainWindowViewModel"/>: exactly one sidebar entry is selected across both lists, and
/// only the page on display is flagged active, so a page in transition cannot capture the Enter key.
/// </summary>
public sealed class MainWindowViewModelTests
{
    [Fact]
    internal void NewShell_ShowsTheCreatePageAsTheOnlyActivePage()
    {
        using var services = DesktopShell.CreateServices();
        var sut = services.GetRequiredService<MainWindowViewModel>();

        Assert.Multiple(
            () => Assert.Same(sut.OperationItems[0], sut.SelectedOperation),
            () => Assert.Null(sut.SelectedUtility),
            () => Assert.Same(sut.OperationItems[0].Page, sut.CurrentPage),
            () => Assert.Equal([sut.CurrentPage], ActivePages(sut)),
            () => Assert.False(string.IsNullOrWhiteSpace(sut.VersionText))
        );
    }

    [Fact]
    internal void SelectingInOneList_ClearsTheOtherAndMovesTheActivePage()
    {
        using var services = DesktopShell.CreateServices();
        var sut = services.GetRequiredService<MainWindowViewModel>();

        sut.SelectedUtility = sut.UtilityItems[1];
        var afterUtility = (sut.SelectedOperation, sut.CurrentPage, ActivePages(sut));

        sut.SelectedOperation = sut.OperationItems[2];

        Assert.Multiple(
            () => Assert.Null(afterUtility.SelectedOperation),
            () => Assert.Same(sut.UtilityItems[1].Page, afterUtility.CurrentPage),
            () => Assert.Equal([sut.UtilityItems[1].Page], afterUtility.Item3),
            () => Assert.Null(sut.SelectedUtility),
            () => Assert.Same(sut.OperationItems[2].Page, sut.CurrentPage),
            () => Assert.Equal([sut.OperationItems[2].Page], ActivePages(sut))
        );
    }

    [Fact]
    internal void ClearingASelection_KeepsTheCurrentPage()
    {
        using var services = DesktopShell.CreateServices();
        var sut = services.GetRequiredService<MainWindowViewModel>();
        var current = sut.CurrentPage;

        sut.SelectedOperation = null;
        sut.SelectedUtility = null;

        Assert.Multiple(
            () => Assert.Same(current, sut.CurrentPage),
            () => Assert.True(current.IsActivePage)
        );
    }

    [Fact]
    internal void NavigationItems_ReadAsTheirLocalizedLabels()
    {
        using var services = DesktopShell.CreateServices();
        var sut = services.GetRequiredService<MainWindowViewModel>();

        Assert.Equal(
            [Strings.NavCreate, Strings.NavUpdate, Strings.NavRestore, Strings.NavVerify, Strings.NavSettings, Strings.NavAbout],
            sut.OperationItems.Concat(sut.UtilityItems).Select(static item => item.ToString())
        );
    }

    /// <summary>
    /// Lists the pages currently flagged as the active page.
    /// </summary>
    /// <param name="shell">The shell.</param>
    /// <returns>The active pages.</returns>
    private static ViewModelBase[] ActivePages(MainWindowViewModel shell)
    {
        return [.. shell.OperationItems.Concat(shell.UtilityItems).Select(static i => i.Page).Where(static p => p.IsActivePage)];
    }
}
