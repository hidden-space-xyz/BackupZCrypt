using CommunityToolkit.Mvvm.ComponentModel;

namespace BackupZCrypt.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public virtual Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }
}
