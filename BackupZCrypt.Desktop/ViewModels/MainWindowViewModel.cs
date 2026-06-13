using System.Collections.ObjectModel;
using BackupZCrypt.Desktop.Messages;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the main window shell: owns the navigation items, the currently displayed page and
/// the version caption, and handles cross-page navigation requests.
/// </summary>
public sealed partial class MainWindowViewModel
    : ViewModelBase,
        IRecipient<NavigateToPageMessage>
{
    [ObservableProperty]
    public partial NavigationItem SelectedItem { get; set; }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class, building the
    /// navigation list and activating the create-backup page.
    /// </summary>
    /// <param name="createBackup">The create-backup page ViewModel.</param>
    /// <param name="updateBackup">The update-backup page ViewModel.</param>
    /// <param name="restoreBackup">The restore-backup page ViewModel.</param>
    /// <param name="settings">The settings page ViewModel.</param>
    /// <param name="about">The about page ViewModel.</param>
    public MainWindowViewModel(
        CreateBackupViewModel createBackup,
        UpdateBackupViewModel updateBackup,
        RestoreBackupViewModel restoreBackup,
        SettingsViewModel settings,
        AboutViewModel about
    )
    {
        NavigationItems =
        [
            new NavigationItem("🔐", Strings.NavCreate, createBackup),
            new NavigationItem("🔄", Strings.NavUpdate, updateBackup),
            new NavigationItem("📦", Strings.NavRestore, restoreBackup),
            new NavigationItem("⚙️", Strings.NavSettings, settings),
            new NavigationItem("ℹ️", Strings.NavAbout, about),
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

    partial void OnSelectedItemChanged(NavigationItem value)
    {
        if (value is null)
        {
            return;
        }

        CurrentPage = value.Page;
        _ = value.Page.OnNavigatedToAsync();
    }
}
