namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// An entry in the shell navigation list, pairing an icon and title with the page to show.
/// </summary>
/// <param name="Icon">The icon displayed next to the title, drawn by the view.</param>
/// <param name="Title">The localized navigation label.</param>
/// <param name="Page">The ViewModel of the page activated when this item is selected.</param>
internal sealed record class NavigationItem(NavigationIcon Icon, string Title, ViewModelBase Page)
{
    /// <summary>
    /// Returns the navigation label, which is also what screen readers announce for the entry.
    /// </summary>
    /// <returns>The navigation label.</returns>
    public override string ToString()
    {
        return this.Title;
    }
}
