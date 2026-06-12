using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Models;

public sealed record NavigationItem(string Icon, string Title, ViewModelBase Page);
