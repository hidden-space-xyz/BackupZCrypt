using System.Globalization;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace BackupZCrypt.Desktop.Animations;

/// <summary>
/// A page transition that removes the outgoing page at once and lets the incoming one fade in while it rises a
/// few pixels into place.
/// </summary>
/// <remarks>
/// <para>
/// Only one page is ever on screen. Cross-fading two unrelated layouts over each other reads as flicker rather
/// than motion, so the outgoing page is hidden as soon as the transition starts.
/// </para>
/// <para>
/// The motion is driven by property transitions instead of keyframe animations: a completed
/// <see cref="Animation.RunAsync(Animatable, CancellationToken)"/> drops its animated values before its caller
/// gets to react, so the resting value can show for a frame; a property transition settles on the value that
/// was set, so neither end of the motion can flash.
/// </para>
/// <para>
/// The motion also starts one frame late on purpose. The transition is started while the incoming page is
/// laid out for the first time, and an animation started then takes the start of that frame as its time zero.
/// Building a page can take a good part of a frame or more, so the first visible step would already be that
/// far along and the entrance would visibly jump. The incoming page is therefore painted fully transparent
/// first, and the rise is queued behind that frame at background priority, so its time zero is the next,
/// fresh frame. A dispatcher job is used rather than an animation-frame request because it runs even when
/// nothing else is animating, and a page left transparent would be far worse than a page that did not move.
/// </para>
/// </remarks>
internal sealed class PageEntranceTransition : IPageTransition
{
    /// <summary>
    /// The deceleration curve of the rise: fast out of the gate, then a long, gentle settle.
    /// </summary>
    private static readonly SplineEasing RiseEasing = new(0.16, 1, 0.3, 1);

    /// <summary>
    /// The curve of the fade, decelerating so the page becomes legible early in the rise.
    /// </summary>
    private static readonly SplineEasing FadeEasing = new(0.2, 0, 0, 1);

    /// <summary>
    /// Gets or sets how long the incoming page takes to settle into place.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(360);

    /// <summary>
    /// Gets or sets how long the incoming page takes to become fully opaque. Shorter than
    /// <see cref="Duration"/>, so the content is readable before the movement ends.
    /// </summary>
    public TimeSpan FadeDuration { get; set; } = TimeSpan.FromMilliseconds(220);

    /// <summary>
    /// Gets or sets the distance, in device-independent pixels, the incoming page rises from.
    /// </summary>
    public double Offset { get; set; } = 14;

    /// <summary>
    /// Hides <paramref name="from"/> and eases <paramref name="to"/> in from below.
    /// </summary>
    /// <param name="from">The presenter of the outgoing page, or <see langword="null"/> on first display.</param>
    /// <param name="to">The presenter of the incoming page.</param>
    /// <param name="forward">Unused: the entrance looks the same in both navigation directions.</param>
    /// <param name="cancellationToken">Signals that a newer navigation superseded this one.</param>
    /// <returns>A task that completes once the incoming page has settled or the transition was superseded.</returns>
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (from is not null)
        {
            from.IsVisible = false;
        }

        if (to is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        to.Transitions = null;
        to.Opacity = 0;
        to.RenderTransform = TranslateY(Offset);
        to.IsVisible = true;

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Rise(to);
                }

                _ = started.TrySetResult();
            },
            DispatcherPriority.Background
        );

        await started.Task;

        await Task.Delay(Duration, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    /// <summary>
    /// Builds a vertical translation. Both ends of the rise use the same single operation, so the transition
    /// interpolates the offset directly instead of reconciling two different operation lists.
    /// </summary>
    /// <param name="offset">The vertical offset in device-independent pixels.</param>
    /// <returns>The translation as transform operations.</returns>
    private static TransformOperations TranslateY(double offset)
    {
        return TransformOperations.Parse(
            string.Create(CultureInfo.InvariantCulture, $"translateY({offset}px)")
        );
    }

    /// <summary>
    /// Puts the page in its resting place without any motion, for a presenter that is not on screen.
    /// </summary>
    /// <param name="page">The presenter of the incoming page.</param>
    private static void Settle(Visual page)
    {
        page.Opacity = 1;
        page.RenderTransform = TranslateY(0);
    }

    /// <summary>
    /// Attaches the fade and rise transitions to the page and moves it to its resting values, which the
    /// transitions then animate towards.
    /// </summary>
    /// <param name="page">The presenter of the incoming page.</param>
    private void Rise(Visual page)
    {
        page.Transitions =
        [
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = FadeDuration,
                Easing = FadeEasing,
            },
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = Duration,
                Easing = RiseEasing,
            },
        ];

        Settle(page);
    }
}
