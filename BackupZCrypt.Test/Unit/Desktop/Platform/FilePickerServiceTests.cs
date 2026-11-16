using Avalonia.Controls;

using BackupZCrypt.Desktop.Platform;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Platform;

/// <summary>
/// UI tests for <see cref="FilePickerService"/>: without a window to host the picker, or when the picker returns
/// no folder, the service reports that nothing was picked instead of failing.
/// </summary>
public sealed class FilePickerServiceTests
{
    [Fact]
    internal Task PickFolderAsync_WithoutAHostWindow_ReturnsNull()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            FilePickerService withoutMainWindow = new();
            FilePickerService withoutTopLevel = new(static () => null);

            Assert.Multiple(
                async () => Assert.Null(await withoutMainWindow.PickFolderAsync("title")),
                async () => Assert.Null(await withoutTopLevel.PickFolderAsync("title"))
            );
        });
    }

    [Fact]
    internal Task PickFolderAsync_WhenThePickerReturnsNoFolder_ReturnsNull()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            var window = HeadlessUi.Show(new TextBlock());

            try
            {
                FilePickerService sut = new(() => window);

                Assert.Null(await sut.PickFolderAsync("title"));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
