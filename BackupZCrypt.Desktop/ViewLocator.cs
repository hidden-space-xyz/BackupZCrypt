using Avalonia.Controls;
using Avalonia.Controls.Templates;
using BackupZCrypt.Desktop.ViewModels;

namespace BackupZCrypt.Desktop;

/// <summary>
/// Resolves a view for a given ViewModel by convention, mapping
/// <c>ViewModels.FooViewModel</c> to <c>Views.FooView</c> by name.
/// </summary>
internal sealed class ViewLocator : IDataTemplate
{
    /// <summary>
    /// Builds the view control that corresponds to the supplied ViewModel instance.
    /// </summary>
    /// <param name="param">The ViewModel to locate a view for.</param>
    /// <returns>
    /// The instantiated view, a <see cref="TextBlock"/> describing the missing view when no
    /// matching type is found, or <see langword="null"/> when <paramref name="param"/> is <see langword="null"/>.
    /// </returns>
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

    /// <summary>
    /// Determines whether this template applies to the supplied data object.
    /// </summary>
    /// <param name="data">The data object to test.</param>
    /// <returns><see langword="true"/> when <paramref name="data"/> is a <see cref="ViewModelBase"/>; otherwise <see langword="false"/>.</returns>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
