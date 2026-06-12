using System.Collections.ObjectModel;
using BackupZCrypt.Desktop.Messages;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace BackupZCrypt.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
    : ViewModelBase,
        IRecipient<NavigateToPageMessage>
{
    [ObservableProperty]
    private NavigationItem selectedItem;

    [ObservableProperty]
    private ViewModelBase currentPage;

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

        currentPage = createBackup;
        selectedItem = NavigationItems[0];
        _ = createBackup.OnNavigatedToAsync();

        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public string VersionText { get; }

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
