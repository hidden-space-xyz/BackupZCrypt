using System.Collections.ObjectModel;

using BackupZCrypt.Desktop.Messages;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the main window shell: owns the navigation items, the currently displayed page, and
/// the version caption, and handles cross-page navigation requests.
/// </summary>
public sealed partial class MainWindowViewModel
    : ViewModelBase,
        IRecipient<NavigateToPageMessage>
{
    /// <summary>
    /// Gets or sets the navigation item currently selected in the sidebar.
    /// </summary>
    [ObservableProperty]
    public partial NavigationItem SelectedItem { get; set; }

    /// <summary>
    /// Gets or sets the ViewModel of the page currently shown in the content area.
    /// </summary>
    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class, building the
    /// navigation list and activating the create-backup page.
    /// </summary>
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
        NavigationItems =
        [
            new NavigationItem(Icons.ShieldLock, Strings.NavCreate, createBackup),
            new NavigationItem(Icons.ArrowSync, Strings.NavUpdate, updateBackup),
            new NavigationItem(Icons.BoxArrowDown, Strings.NavRestore, restoreBackup),
            new NavigationItem(Icons.ShieldCheck, Strings.NavVerify, verifyBackup),
            new NavigationItem(Icons.Settings, Strings.NavSettings, settings),
            new NavigationItem(Icons.Info, Strings.NavAbout, about),
        ];

        VersionText = about.VersionText;
        CurrentPage = createBackup;
        SelectedItem = NavigationItems[0];
        _ = createBackup.OnNavigatedToAsync();

        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// Gets the navigation entries shown in the shell sidebar.
    /// </summary>
    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>
    /// Gets the formatted application version caption.
    /// </summary>
    public string VersionText { get; }

    /// <summary>
    /// Handles a navigation request by selecting the navigation item whose page matches the requested type.
    /// </summary>
    /// <param name="message">The navigation request.</param>
    public void Receive(NavigateToPageMessage message)
    {
        var target = NavigationItems.FirstOrDefault(item =>
            item.Page.GetType() == message.PageType
        );

        if (target is not null)
        {
            SelectedItem = target;
        }
    }

    /// <summary>
    /// Swaps the displayed page when the sidebar selection changes, moving the active-page flag to the
    /// incoming page and letting it run its on-navigation work.
    /// </summary>
    /// <param name="value">The newly selected navigation item.</param>
    partial void OnSelectedItemChanged(NavigationItem value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage.IsActivePage = false;
        value.Page.IsActivePage = true;
        CurrentPage = value.Page;
        _ = value.Page.OnNavigatedToAsync();
    }
}
