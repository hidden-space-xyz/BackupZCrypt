using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// Base class for all ViewModels, providing change notification and a navigation activation hook.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Invoked when this ViewModel's page becomes the active page. Override to perform on-navigation work.
    /// </summary>
    /// <returns>A task that completes once activation work has finished. The default implementation completes immediately.</returns>
    public virtual Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }
}
