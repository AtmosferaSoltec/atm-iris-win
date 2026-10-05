using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Iris.DesignSystem;

/// <summary>IrisMotion tokens (IRIS_SPEC §4.7). Honors Windows "Animation effects".</summary>
public static class Motion
{
    private static readonly UISettings Settings = new();

    public static readonly TimeSpan Snappy = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan Smooth = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan Gentle = TimeSpan.FromMilliseconds(700);

    public static bool AnimationsEnabled => Settings.AnimationsEnabled;

    public static TimeSpan Duration(TimeSpan duration) => AnimationsEnabled ? duration : TimeSpan.Zero;

    /// <summary>Animates the element's opacity; completes immediately when animations are off.</summary>
    public static Storyboard Fade(UIElement element, double to, TimeSpan duration, Action? completed = null)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        var storyboard = new Storyboard { Children = { animation } };
        if (completed is not null)
        {
            storyboard.Completed += (_, _) => completed();
        }

        storyboard.Begin();
        return storyboard;
    }

    /// <summary>Animates a property of a transform/object (e.g. TranslateTransform.X).</summary>
    public static void Animate(DependencyObject target, string property, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        new Storyboard { Children = { animation } }.Begin();
    }
}
