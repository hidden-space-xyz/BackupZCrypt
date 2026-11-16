using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;

using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop;
using BackupZCrypt.Desktop.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Builds the Desktop container the application runs, with the settings store kept in memory and the folder
/// picker and clipboard substituted, so UI tests exercise the real pages without touching the user's files.
/// </summary>
internal static class DesktopShell
{
    /// <summary>
    /// Builds the container.
    /// </summary>
    /// <param name="configure">Further registrations applied after the test doubles, or <see langword="null"/>.</param>
    /// <returns>A provider the caller owns and disposes.</returns>
    public static ServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        return App.ConfigureServices(services =>
        {
            _ = services.AddSingleton<ISettingsStore>(new InMemorySettingsStore());
            _ = services.AddSingleton(Substitute.For<IFilePickerService>());
            _ = services.AddSingleton(Substitute.For<IClipboardService>());
            configure?.Invoke(services);
        });
    }

    /// <summary>
    /// Finds the button that screen readers announce with the given name.
    /// </summary>
    /// <param name="root">The control to search under.</param>
    /// <param name="automationName">The button's accessible name.</param>
    /// <returns>The button.</returns>
    public static Button FindButton(ILogical root, string automationName)
    {
        return root.GetLogicalDescendants()
            .OfType<Button>()
            .First(button => AutomationProperties.GetName(button) == automationName);
    }
}
