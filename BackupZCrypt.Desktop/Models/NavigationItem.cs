using Avalonia.Media;

using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// An entry in the shell navigation list, pairing a display icon and title with the page to show.
/// </summary>
/// <param name="Icon">The vector geometry displayed next to the title.</param>
/// <param name="Title">The localized navigation label.</param>
/// <param name="Page">The ViewModel of the page activated when this item is selected.</param>
internal sealed record NavigationItem(StreamGeometry Icon, string Title, ViewModelBase Page);
