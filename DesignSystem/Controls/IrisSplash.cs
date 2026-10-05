using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Iris.DesignSystem.Controls;

/// <summary>
/// Launch splash (IRIS_SPEC §6.0) — the only place the logo moves. The ring enters from −160°
/// at 82% scale and zero opacity with a soft spring (1.1 s); the glint fades in at 0.75 s; at
/// 1.6 s the whole layer fades out (smooth) and <see cref="Finished"/> fires. With Windows
/// animation effects off, it only fades the mark in (0.4 s) and leaves the same way.
/// </summary>
public sealed partial class IrisSplash : UserControl
{
    private const double MarkSize = 160;

    private readonly IrisMark _mark = new() { Width = MarkSize, Height = MarkSize };
    private readonly RotateTransform _ringRotation = new();
    private readonly ScaleTransform _markScale = new();

    public IrisSplash()
    {
        IsTabStop = false;
        AutomationProperties.SetName(this, "Iris");
        _mark.HorizontalAlignment = HorizontalAlignment.Center;
        _mark.VerticalAlignment = VerticalAlignment.Center;
        _mark.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        _mark.RenderTransform = _markScale;
        _mark.Ring.RenderTransform = _ringRotation;
        Content = new Grid { Background = IrisTheme.Brush("IrisCanvasBrush"), Children = { _mark } };
        Loaded += (_, _) => Play();
    }

    public event EventHandler? Finished;

    private void Play()
    {
        var storyboard = new Storyboard();
        if (Motion.AnimationsEnabled)
        {
            var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 };
            var enter = TimeSpan.FromSeconds(1.1);
            _ringRotation.Angle = -160;
            _markScale.ScaleX = _markScale.ScaleY = 0.82;
            _mark.Opacity = 0;
            _mark.Glint.Opacity = 0;
            storyboard.Children.Add(Animation(_ringRotation, nameof(RotateTransform.Angle), 0, enter, TimeSpan.Zero, spring));
            storyboard.Children.Add(Animation(_markScale, nameof(ScaleTransform.ScaleX), 1, enter, TimeSpan.Zero, spring));
            storyboard.Children.Add(Animation(_markScale, nameof(ScaleTransform.ScaleY), 1, enter, TimeSpan.Zero, spring));
            storyboard.Children.Add(Animation(_mark, nameof(Opacity), 1, enter, TimeSpan.Zero, new CubicEase { EasingMode = EasingMode.EaseOut }));
            storyboard.Children.Add(Animation(_mark.Glint, nameof(Opacity), 1, TimeSpan.FromSeconds(0.35), TimeSpan.FromSeconds(0.75), new CubicEase { EasingMode = EasingMode.EaseOut }));
        }
        else
        {
            _mark.Opacity = 0;
            storyboard.Children.Add(Animation(_mark, nameof(Opacity), 1, TimeSpan.FromSeconds(0.4), TimeSpan.Zero, null));
        }

        // Leave at 1.6 s with the smooth fade (kept even without animation effects, per spec).
        storyboard.Children.Add(Animation(this, nameof(Opacity), 0, Motion.Smooth, TimeSpan.FromSeconds(1.6), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        storyboard.Completed += (_, _) => Finished?.Invoke(this, EventArgs.Empty);
        storyboard.Begin();
    }

    private static DoubleAnimation Animation(DependencyObject target, string property, double to, TimeSpan duration, TimeSpan delay, EasingFunctionBase? easing)
    {
        var animation = new DoubleAnimation { To = to, Duration = duration, BeginTime = delay, EasingFunction = easing };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
