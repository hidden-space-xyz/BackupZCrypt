using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop;

internal sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var name = param
            .GetType()
            .FullName!.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        return type is null
            ? new TextBlock { Text = "Not Found: " + name }
            : (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
