using Iris.Core.Timing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Iris.DesignSystem;

/// <summary>Small x:Bind helpers for visibility and data-driven brushes.</summary>
public static class Bind
{
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    /// <summary>Hero glow: the service color, bottom-left.</summary>
    public static Brush HeroGlow(string hex) => IrisTheme.Glow(IrisTheme.ParseHex(hex), 0.42, new Windows.Foundation.Point(0, 1), 0.75, 0.95);

    /// <summary>Block clock: primary (normal) · warning (about to pass) · danger (over).</summary>
    public static Brush ClockBrush(ClockState state) => IrisTheme.Brush(state switch
    {
        ClockState.Warning => "IrisWarningBrush",
        ClockState.Over => "IrisDangerBrush",
        _ => "IrisTextPrimaryBrush",
    });

    /// <summary>Block progress bar: success · warning · danger.</summary>
    public static Brush ProgressBrush(ClockState state) => IrisTheme.Brush(state switch
    {
        ClockState.Warning => "IrisWarningBrush",
        ClockState.Over => "IrisDangerBrush",
        _ => "IrisSuccessBrush",
    });

    public static Brush OverBrush(bool isOver) => IrisTheme.Brush(isOver ? "IrisDangerBrush" : "IrisSuccessBrush");

    public static Windows.UI.Color OverColor(bool isOver) => IrisTheme.Color(isOver ? "IrisDangerColor" : "IrisSuccessColor");

    public static Windows.UI.Color Color(string key) => IrisTheme.Color(key);

    /// <summary>Solid brush for a color token key ("IrisCoralColor").</summary>
    public static Brush ColorBrush(string key) => new SolidColorBrush(IrisTheme.Color(key));

    /// <summary>A color token at <paramref name="opacity"/> (tinted icon boxes: 14%).</summary>
    public static Brush ColorTint(string key, double opacity) => IrisTheme.Tint(IrisTheme.Color(key), opacity);
}
