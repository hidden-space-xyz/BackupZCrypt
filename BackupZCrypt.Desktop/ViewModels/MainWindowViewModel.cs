using System.Collections.ObjectModel;

using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;

using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the main window shell: owns the navigation items, the currently displayed page, and
/// the version caption.
/// </summary>
/// <remarks>
/// The sidebar shows two lists: the four backup operations at the top and the application pages (settings
/// and help) pinned to the bottom. Each list has its own selection, and selecting in one clears the other, so
/// exactly one entry is highlighted across both.
/// </remarks>
internal sealed partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// Gets or sets the backup operation currently selected in the sidebar, or <see langword="null"/> while an
    /// application page is shown instead.
    /// </summary>
    /// <remarks>
    /// The bound <c>ListBox</c> can also clear its selection transiently while its items are being rebuilt; the
    /// change handler ignores every <see langword="null"/>, so only a real selection navigates.
    /// </remarks>
    [ObservableProperty]
    public partial NavigationItem? SelectedOperation { get; set; }

    /// <summary>
    /// Gets or sets the application page currently selected in the sidebar, or <see langword="null"/> while a
    /// backup operation is shown instead.
    /// </summary>
    [ObservableProperty]
    public partial NavigationItem? SelectedUtility { get; set; }

    /// <summary>
    /// Gets or sets the ViewModel of the page currently shown in the content area.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class, building the
    /// navigation lists and activating the create-backup page.
    /// </summary>
    /// <remarks>
    /// Assigning the initial selection is what activates the first page: the change handler sets the
    /// active-page flag and starts that page's on-navigation work. Calling
    /// <see cref="ViewModelBase.OnNavigatedToAsync"/> here as well would run it a second time.
    /// </remarks>
    /// <param name="createBackup">The create-backup page ViewModel.</param>
    /// <param name="updateBackup">The update-backup page ViewModel.</param>
    /// <param name="restoreBackup">The restore-backup page ViewModel.</param>
    /// <param name="verifyBackup">The verify-backup page ViewModel.</param>
    /// <param name="settings">The settings page ViewModel.</param>
    /// <param name="about">The about page ViewModel.</param>
    public MainWindowViewModel(
        CreateBackupViewModel createBackup,
        UpdateBackupViewModel updateBackup,
        RestoreBackupViewModel restoreBackup,
        VerifyBackupViewModel verifyBackup,
        SettingsViewModel settings,
        AboutViewModel about
    )
    {
        ArgumentNullException.ThrowIfNull(about);

        OperationItems =
        [
            new NavigationItem(Icons.ShieldLock, Strings.NavCreate, createBackup),
            new NavigationItem(Icons.ArrowSync, Strings.NavUpdate, updateBackup),
            new NavigationItem(Icons.BoxArrowDown, Strings.NavRestore, restoreBackup),
            new NavigationItem(Icons.ShieldCheck, Strings.NavVerify, verifyBackup),
        ];

        UtilityItems =
        [
            new NavigationItem(Icons.Settings, Strings.NavSettings, settings),
            new NavigationItem(Icons.Info, Strings.NavAbout, about),
        ];

        VersionText = about.VersionText;
        CurrentPage = createBackup;

        SelectedOperation = OperationItems[0];
    }

    /// <summary>
    /// Gets the backup operations listed at the top of the sidebar.
    /// </summary>
    public ObservableCollection<NavigationItem> OperationItems { get; }

    /// <summary>
    /// Gets the application pages pinned to the bottom of the sidebar.
    /// </summary>
    public ObservableCollection<NavigationItem> UtilityItems { get; }

    /// <summary>
    /// Gets the formatted application version caption.
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// Clears the application-page selection and shows the chosen operation.
    /// </summary>
    /// <param name="value">The newly selected operation, or <see langword="null"/> while the selection is being cleared.</param>
    partial void OnSelectedOperationChanged(NavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedUtility = null;
        Navigate(value);
    }

    /// <summary>
    /// Clears the operation selection and shows the chosen application page.
    /// </summary>
    /// <param name="value">The newly selected page, or <see langword="null"/> while the selection is being cleared.</param>
    partial void OnSelectedUtilityChanged(NavigationItem? value)
    {
        if (value is null)
        {
            return;
        }

        SelectedOperation = null;
        Navigate(value);
    }

    /// <summary>
    /// Swaps the displayed page, moving the active-page flag to the incoming page and letting it run its
    /// on-navigation work.
    /// </summary>
    /// <param name="item">The navigation entry whose page becomes current.</param>
    private void Navigate(NavigationItem item)
    {
        CurrentPage.IsActivePage = false;
        item.Page.IsActivePage = true;
        CurrentPage = item.Page;
        _ = item.Page.OnNavigatedToAsync();
    }
}
