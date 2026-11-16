using Avalonia.Controls;
using Avalonia.Media.Transformation;

using BackupZCrypt.Desktop.Animations;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Animations;

/// <summary>
/// UI tests for <see cref="PageEntranceTransition"/>: only the incoming page is ever visible, it settles fully
/// opaque in its resting place, and a superseded transition leaves it alone.
/// </summary>
public sealed class PageEntranceTransitionTests
{
    [Fact]
    internal Task Start_HidesTheOutgoingPageAndSettlesTheIncomingOne()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            Border from = new();
            Border to = new() { IsVisible = false };
            var window = HeadlessUi.Show(new Panel { Children = { from, to } });
            PageEntranceTransition sut = new()
            {
                Duration = TimeSpan.FromMilliseconds(20),
                FadeDuration = TimeSpan.FromMilliseconds(20),
            };

            await sut.Start(from, to, forward: true, CancellationToken.None);
            await HeadlessUi.WaitUntilAsync(() => to.Opacity == 1);

            Assert.Multiple(
                () => Assert.False(from.IsVisible),
                () => Assert.True(to.IsVisible),
                () => Assert.Equal(1, to.Opacity),
                () => Assert.Equal(2, to.Transitions!.Count),
                () => Assert.Equal(0, ((TransformOperations)to.RenderTransform!).Value.M32)
            );
            window.Close();
        });
    }

    [Fact]
    internal Task Start_AlreadySupersededOrWithoutIncomingPage_OnlyHidesTheOutgoingPage()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            Border from = new();
            Border to = new() { IsVisible = false };
            PageEntranceTransition sut = new();

            await sut.Start(from, null, forward: true, CancellationToken.None);
            await sut.Start(null, to, forward: false, new CancellationToken(canceled: true));

            Assert.Multiple(
                () => Assert.False(from.IsVisible),
                () => Assert.False(to.IsVisible),
                () => Assert.Equal(1, to.Opacity)
            );
        });
    }

    [Fact]
    internal Task Start_SupersededBeforeTheRise_LeavesThePageWhereItStarted()
    {
        return HeadlessUi.RunAsync(async () =>
        {
            Border to = new();
            using CancellationTokenSource superseded = new();
            PageEntranceTransition sut = new() { Offset = 14 };

            var transition = sut.Start(null, to, forward: true, superseded.Token);
            await superseded.CancelAsync();
            await transition;

            Assert.Multiple(
                () => Assert.Equal(0, to.Opacity),
                () => Assert.Null(to.Transitions),
                () => Assert.Equal(14, ((TransformOperations)to.RenderTransform!).Value.M32)
            );
        });
    }
}
