using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iris.DesignSystem.Controls;

/// <summary>
/// The iris symbol (IRIS_SPEC §5.2): the open spectrum ring and the white glint, two SVG layers
/// that share the same 1000×1000 box so they line up exactly. Static everywhere; only the
/// splash animates it, through <see cref="Ring"/> and <see cref="Glint"/>.
/// </summary>
public sealed partial class IrisMark : UserControl
{
    public static readonly Uri RingSource = new("ms-appx:///Assets/Logo/iris-anillo.svg");
    public static readonly Uri GlintSource = new("ms-appx:///Assets/Logo/iris-brillo.svg");

    public IrisMark()
    {
        IsTabStop = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Ring = new Image { Source = new SvgImageSource(RingSource), Stretch = Stretch.Uniform, RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5) };
        Glint = new Image { Source = new SvgImageSource(GlintSource), Stretch = Stretch.Uniform };
        Content = new Grid { Children = { Ring, Glint } };
    }

    /// <summary>The ring layer (the splash rotates only this one).</summary>
    public Image Ring { get; }

    /// <summary>The glint layer; it never rotates.</summary>
    public Image Glint { get; }
}
