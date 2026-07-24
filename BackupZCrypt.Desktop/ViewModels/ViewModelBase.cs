using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Base class for all ViewModels, providing change notification and a navigation activation hook.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether this ViewModel's page is the one currently shown in
    /// the shell. Views bind their default-button state to this so the outgoing page cannot capture
    /// the Enter key during the navigation transition.
    /// </summary>
    [ObservableProperty]
    public partial bool IsActivePage { get; set; }

    /// <summary>
    /// Invoked when this ViewModel's page becomes the active page. Override to perform on-navigation work.
    /// </summary>
    /// <returns>A task that completes once activation work has finished. The default implementation completes immediately.</returns>
    public virtual Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }
}
