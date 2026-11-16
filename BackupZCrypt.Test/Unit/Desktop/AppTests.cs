using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Tests for <see cref="App"/>: the container it builds must resolve the whole shell, and start-up must show the
/// main window in the saved language without ever failing because that preference cannot be used.
/// </summary>
public sealed class AppTests
{
    [Fact]
    internal void ConfigureServices_ResolvesTheShellAndEveryPageAsSingletons()
    {
        using var services = App.ConfigureServices(
            static s => s.AddSingleton<ISettingsStore>(new InMemorySettingsStore())
        );

        var shell = services.GetRequiredService<MainWindowViewModel>();

        Assert.Multiple(
            () => Assert.Same(shell, services.GetRequiredService<MainWindowViewModel>()),
            () => Assert.IsType<CreateBackupViewModel>(shell.CurrentPage),
            () => Assert.Equal(4, shell.OperationItems.Count),
            () => Assert.Equal(2, shell.UtilityItems.Count),
            () => Assert.Same(services.GetRequiredService<SettingsViewModel>(), shell.UtilityItems[0].Page),
            () => Assert.Same(services.GetRequiredService<AboutViewModel>(), shell.UtilityItems[1].Page)
        );
    }

    [Fact]
    internal Task OnFrameworkInitializationCompleted_ShowsTheShellInTheSavedLanguage()
    {
        InMemorySettingsStore store = new();
        store.Seed(new LanguageSettings("es"));

        return WithDesktopLifetimeAsync(
            store,
            lifetime =>
            {
                Assert.Multiple(
                    () => Assert.IsType<MainWindowViewModel>(Assert.IsType<MainWindow>(lifetime.MainWindow).DataContext),
                    () => Assert.Equal("es", CultureInfo.CurrentUICulture.Name),
                    () => Assert.Equal("es", CultureInfo.DefaultThreadCurrentUICulture?.Name)
                );
            }
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("!!")]
    internal Task OnFrameworkInitializationCompleted_WithoutAUsableLanguage_KeepsTheSystemCulture(string? code)
    {
        InMemorySettingsStore store = new();
        store.Seed(new LanguageSettings(code));

        return WithDesktopLifetimeAsync(store, AssertKeptSystemCulture);
    }

    [Fact]
    internal Task OnFrameworkInitializationCompleted_WhenTheLanguageCannotBeRead_KeepsTheSystemCulture()
    {
        InMemorySettingsStore store = new() { LoadFailure = new IOException("unreadable") };

        return WithDesktopLifetimeAsync(store, AssertKeptSystemCulture);
    }

    [Fact]
    internal Task OnFrameworkInitializationCompleted_WithoutADesktopLifetime_CreatesNoWindow()
    {
        return HeadlessUi.RunAsync(() =>
        {
            var lifetime = Substitute.For<ISingleViewApplicationLifetime>();
            App app = new() { ApplicationLifetime = lifetime };

            app.OnFrameworkInitializationCompleted();

            Assert.Null(lifetime.MainView);
        });
    }

    [Fact]
    internal void BuildAvaloniaApp_ConfiguresTheApplicationType()
    {
        var builder = BackupZCrypt.Desktop.Program.BuildAvaloniaApp();

        Assert.Equal(typeof(App), builder.ApplicationType);
    }

    /// <summary>
    /// Asserts that start-up left the UI culture captured before it ran untouched.
    /// </summary>
    /// <param name="lifetime">The lifetime start-up ran with.</param>
    private static void AssertKeptSystemCulture(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        Assert.Multiple(
            () => Assert.IsType<MainWindow>(lifetime.MainWindow),
            () => Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentUICulture)
        );
    }

    /// <summary>
    /// Runs start-up against a substituted desktop lifetime and settings store, restores the cultures afterwards, and checks that exiting the lifetime disposes the container.
    /// </summary>
    /// <param name="store">The settings store start-up reads the language from.</param>
    /// <param name="assert">The assertions made once start-up has finished.</param>
    /// <returns>A task that completes once the test has run on the UI thread.</returns>
    private static Task WithDesktopLifetimeAsync(
        InMemorySettingsStore store,
        Action<IClassicDesktopStyleApplicationLifetime> assert
    )
    {
        return HeadlessUi.RunAsync(() =>
        {
            var lifetime = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
            App app = new()
            {
                ApplicationLifetime = lifetime,
                ServiceOverrides = s => s.AddSingleton<ISettingsStore>(store),
            };
            var previousUiCulture = CultureInfo.CurrentUICulture;
            var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            try
            {
                app.OnFrameworkInitializationCompleted();
                assert(lifetime);

                lifetime.Exit += Raise.EventWith(new ControlledApplicationLifetimeExitEventArgs(0));
                (lifetime.MainWindow as Window)?.Close();
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousUiCulture;
                CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
            }
        });
    }
}
