using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using BackupZCrypt.Desktop;
using BackupZCrypt.Desktop.Controls;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Desktop.Views;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Desktop.Views;

/// <summary>
/// UI tests for the page views: each page lists every option its ViewModel offers, binds its password boxes,
/// and can be walked with the keyboard.
/// </summary>
public sealed class PageViewsTests
{
    [Fact]
    internal Task SettingsView_ShowsEveryOptionTheSettingsOffer()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var page = services.GetRequiredService<SettingsViewModel>();
            await page.OnNavigatedToAsync();
            var window = HeadlessUi.Show(new SettingsView { DataContext = page });

            try
            {
                var lists = window.GetLogicalDescendants().OfType<ListBox>().ToArray();
                await HeadlessUi.WaitUntilAsync(() => lists.All(static l => l.GetRealizedContainers().Count() == l.ItemCount));

                Assert.Multiple(
                    () => Assert.Contains(lists, l => ReferenceEquals(l.ItemsSource, page.EncryptionOptions)),
                    () => Assert.Contains(lists, l => ReferenceEquals(l.ItemsSource, page.KeyDerivationOptions)),
                    () => Assert.Contains(lists, l => ReferenceEquals(l.ItemsSource, page.CompressionOptions)),
                    () => Assert.Contains(lists, l => ReferenceEquals(l.ItemsSource, page.LanguageOptions)),
                    () => Assert.Contains(lists, l => ReferenceEquals(l.ItemsSource, page.DataUnitOptions)),
                    () => Assert.All(lists, static l => Assert.NotNull(l.SelectedItem))
                );
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    internal Task AboutView_DescribesEveryAlgorithm()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var page = services.GetRequiredService<AboutViewModel>();
            var window = HeadlessUi.Show(new AboutView { DataContext = page });

            try
            {
                var names = page.EncryptionAlgorithms.Concat(page.KeyDerivationAlgorithms)
                    .Concat(page.CompressionAlgorithms)
                    .Select(static a => a.Name)
                    .ToArray();

                await HeadlessUi.WaitUntilAsync(() =>
                {
                    var shown = window.GetLogicalDescendants().OfType<TextBlock>().Select(static t => t.Text).ToHashSet();
                    return names.All(shown.Contains);
                });
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(typeof(CreateBackupViewModel), 2)]
    [InlineData(typeof(UpdateBackupViewModel), 1)]
    [InlineData(typeof(RestoreBackupViewModel), 1)]
    [InlineData(typeof(VerifyBackupViewModel), 1)]
    internal Task OperationPages_BindTheirPasswordBoxesAndCanBeWalkedWithTheKeyboard(Type pageType, int passwordBoxes)
    {
        return HeadlessUi.RunAsync(async () =>
        {
            using var services = DesktopShell.CreateServices();
            var page = (ViewModelBase)services.GetRequiredService(pageType);
            await page.OnNavigatedToAsync();
            page.IsActivePage = true;
            var view = new ViewLocator().Build(page)!;
            view.DataContext = page;
            var window = HeadlessUi.Show(view);

            try
            {
                var boxes = window.GetLogicalDescendants().OfType<PasswordTextBox>().Take(passwordBoxes).ToArray();
                foreach (var box in boxes)
                {
                    box.Text = "correct horse battery";
                }

                HashSet<IInputElement> visited = [];
                for (var i = 0; i < 20; i++)
                {
                    window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                    Dispatcher.UIThread.RunJobs();
                    if (window.FocusManager?.GetFocusedElement() is { } focused)
                    {
                        _ = visited.Add(focused);
                    }
                }

                Assert.Multiple(
                    () => Assert.Equal(passwordBoxes, boxes.Length),
                    () => Assert.NotEmpty(visited),
                    () => Assert.Equal("correct horse battery", page.GetType().GetProperty("Password")!.GetValue(page))
                );
            }
            finally
            {
                window.Close();
            }
        });
    }
}
