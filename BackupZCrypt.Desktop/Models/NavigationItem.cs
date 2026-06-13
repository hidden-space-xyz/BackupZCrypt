using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// An entry in the shell navigation list, pairing a display icon and title with the page to show.
/// </summary>
/// <param name="Icon">The glyph displayed next to the title.</param>
/// <param name="Title">The localized navigation label.</param>
/// <param name="Page">The ViewModel of the page activated when this item is selected.</param>
public sealed record NavigationItem(string Icon, string Title, ViewModelBase Page);
