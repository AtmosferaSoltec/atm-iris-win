using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iris.DesignSystem.Controls;

/// <summary>Symbol + "iris" lettering in one SVG (IRIS_SPEC §5.2), scaled to a fixed height.</summary>
public sealed partial class IrisWordmark : UserControl
{
    /// <summary>Width : height of iris-logo.svg.</summary>
    private const double AspectRatio = 2.564;

    public static readonly DependencyProperty MarkSizeProperty =
        DependencyProperty.Register(nameof(MarkSize), typeof(double), typeof(IrisWordmark), new PropertyMetadata(30.0, (d, _) => ((IrisWordmark)d).ApplySize()));

    private readonly Image _image = new()
    {
        Source = new SvgImageSource(new Uri("ms-appx:///Assets/Logo/iris-logo.svg")),
        Stretch = Stretch.Uniform,
    };

    public IrisWordmark()
    {
        IsTabStop = false;
        AutomationProperties.SetName(this, "Iris");
        Content = _image;
        ApplySize();
    }

    /// <summary>Height of the wordmark (30 by default; 24 in the top bar).</summary>
    public double MarkSize
    {
        get => (double)GetValue(MarkSizeProperty);
        set => SetValue(MarkSizeProperty, value);
    }

    private void ApplySize()
    {
        _image.Height = MarkSize;
        _image.Width = MarkSize * AspectRatio;
    }
}
