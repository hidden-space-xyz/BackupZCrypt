using System.Globalization;

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Composition;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Desktop;

/// <summary>
/// The Avalonia application root: loads XAML, builds the dependency-injection container, applies the
/// language preference, and shows the main window.
/// </summary>
internal sealed class App : Avalonia.Application
{
    /// <summary>
    /// Loads the application's XAML resources.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Completes framework initialization by wiring up services, applying the saved language, and
    /// creating the main window for the classic desktop lifetime.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = ConfigureServices();
            desktop.Exit += (_, _) => services.Dispose();

            ApplyLanguagePreference(services);

            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the dependency-injection container holding the domain and application services, the
    /// Desktop-only platform services, and the ViewModel of the shell and of every page.
    /// </summary>
    /// <returns>The built provider, disposed when the desktop lifetime exits.</returns>
    private static ServiceProvider ConfigureServices()
    {
        ServiceCollection services = [];

        _ = services.AddBackupZCryptServices();

        _ = services.AddSingleton<IFilePickerService, FilePickerService>();
        _ = services.AddSingleton<IClipboardService, ClipboardService>();

        _ = services.AddSingleton<CreateBackupViewModel>();
        _ = services.AddSingleton<UpdateBackupViewModel>();
        _ = services.AddSingleton<RestoreBackupViewModel>();
        _ = services.AddSingleton<VerifyBackupViewModel>();
        _ = services.AddSingleton<SettingsViewModel>();
        _ = services.AddSingleton<AboutViewModel>();
        _ = services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }

    /// <summary>
    /// Applies the language stored in <see cref="LanguageSettings"/> to the current and default UI culture.
    /// </summary>
    /// <remarks>
    /// Failures are deliberately swallowed: a missing, unreadable, or invalid language preference must leave the
    /// application on the system default UI culture rather than block startup.
    /// </remarks>
    /// <param name="services">The provider used to resolve the language settings handler.</param>
    private static void ApplyLanguagePreference(ServiceProvider services)
    {
        var culture = TryResolvePreferredCulture(services);

        if (culture is null)
        {
            return;
        }

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>
    /// Reads the stored language preference and turns it into a culture.
    /// </summary>
    /// <param name="services">The provider used to resolve the language settings handler.</param>
    /// <returns>
    /// The preferred culture, or <see langword="null"/> when no preference is stored or it cannot be read.
    /// </returns>
    private static CultureInfo? TryResolvePreferredCulture(ServiceProvider services)
    {
        try
        {
            var languageQuery = services.GetRequiredService<
                IQueryHandler<GetSettingsQuery<LanguageSettings>, LanguageSettings>
            >();

            var language = Task.Run(
                    () => languageQuery.HandleAsync(new GetSettingsQuery<LanguageSettings>())
                )
                .GetAwaiter()
                .GetResult();

            return string.IsNullOrWhiteSpace(language.LanguageCode)
                ? null
                : new CultureInfo(language.LanguageCode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
