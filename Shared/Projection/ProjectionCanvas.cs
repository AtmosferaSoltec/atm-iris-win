using System;
using System.Linq;
using Iris.Core.Models;
using Iris.DesignSystem;
using Iris.DesignSystem.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Iris.Shared.Projection;

/// <summary>
/// The one projection renderer (IRIS_SPEC §5.12), used by slide thumbnails, the EN VIVO preview
/// and the TV window so they always look identical. Draws a 16:9 stage in a fixed 1600×900
/// coordinate space (so every measurement is a fraction of the width W) and scales it uniformly.
/// </summary>
public sealed partial class ProjectionCanvas : UserControl
{
    private const double W = 1600;
    private const double H = 900;

    public static readonly DependencyProperty FrameProperty =
        DependencyProperty.Register(nameof(Frame), typeof(ProjectionFrame), typeof(ProjectionCanvas), new PropertyMetadata(null, (d, e) => ((ProjectionCanvas)d).Render((ProjectionFrame?)e.OldValue)));

    /// <summary>Cross-fade between frames (live preview and TV). Thumbnails switch instantly.</summary>
    public static readonly DependencyProperty UsesTransitionsProperty =
        DependencyProperty.Register(nameof(UsesTransitions), typeof(bool), typeof(ProjectionCanvas), new PropertyMetadata(false));

    private readonly Grid _stage = new() { Width = W, Height = H };

    public ProjectionCanvas()
    {
        IsTabStop = false;
        Content = new Grid
        {
            Background = IrisTheme.Brush("IrisBlackBrush"),
            Children = { new Viewbox { Stretch = Stretch.Uniform, Child = _stage } },
        };
    }

    public ProjectionFrame? Frame
    {
        get => (ProjectionFrame?)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public bool UsesTransitions
    {
        get => (bool)GetValue(UsesTransitionsProperty);
        set => SetValue(UsesTransitionsProperty, value);
    }

    /// <summary>Keeps a 16:9 aspect ratio driven by the available width.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = availableSize.Width, height;
        if (double.IsInfinity(width))
        {
            width = double.IsInfinity(availableSize.Height) ? 320 : availableSize.Height * 16 / 9;
        }

        height = width * 9 / 16;
        if (!double.IsInfinity(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height * 16 / 9;
        }

        var size = new Size(width, height);
        base.MeasureOverride(size);
        return size;
    }

    private void Render(ProjectionFrame? previous)
    {
        var frame = Frame ?? ProjectionFrame.Black;
        if (Equals(previous, frame) && _stage.Children.Count > 0)
        {
            return;
        }

        AutomationProperties.SetName(this, frame.Content switch
        {
            TextContent text => text.Body,
            ImageContent image => image.Title,
            VideoContent video => video.Title,
            AudioContent audio => audio.Title,
            LogoContent logo => logo.Name,
            _ => "Pantalla vacía",
        });

        var layer = BuildLayer(frame);
        var animate = UsesTransitions && IsLoaded && Motion.AnimationsEnabled && _stage.Children.Count > 0;

        // Keep the background still when only the content changes, so the cross-fade only touches the text.
        if (!animate)
        {
            _stage.Children.Clear();
            _stage.Children.Add(layer);
            return;
        }

        var outgoing = _stage.Children.ToList();
        layer.Opacity = 0;
        _stage.Children.Add(layer);
        Motion.Fade(layer, 1, Motion.Smooth, () =>
        {
            foreach (var old in outgoing)
            {
                _stage.Children.Remove(old);
            }
        });
    }

    private static Grid BuildLayer(ProjectionFrame frame)
    {
        var layer = new Grid { Width = W, Height = H, Background = IrisTheme.Brush("IrisBlackBrush") };

        if (frame.Background is { } background)
        {
            layer.Children.Add(new Rectangle { Fill = IrisTheme.DiagonalGradient(background.Colors) });
            layer.Children.Add(new Rectangle { Fill = IrisTheme.Glow(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.14, new Point(0.5, 0), 0.9, 0.7) });
            layer.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)) });
        }

        var content = frame.Content switch
        {
            TextContent text => BuildText(text),
            ImageContent image => BuildImage(image),
            VideoContent video => BuildMedia(Glyphs.Play, IrisTheme.Color("IrisTextPrimaryColor"), video.Title, video.Duration, circled: true),
            AudioContent audio => BuildMedia(Glyphs.Waveform, IrisTheme.Color("IrisSuccessColor"), audio.Title, audio.Duration, circled: false),
            LogoContent logo => BuildLogo(logo),
            _ => null,
        };

        if (content is not null)
        {
            layer.Children.Add(content);
        }

        return layer;
    }

    // Letra / versículo: serif Medium W×0.046, centered, shrinks to fit; footnote sans W×0.022 uppercase at 60%.
    private static UIElement BuildText(TextContent text)
    {
        const double padding = W * 0.07;
        var body = new TextBlock
        {
            Text = text.Body,
            Width = W - (padding * 2),
            FontFamily = (FontFamily)Application.Current.Resources["IrisSerifDisplayFontFamily"],
            FontWeight = FontWeights.Medium,
            FontSize = W * 0.046,
            LineHeight = (W * 0.046 * 1.2) + (W * 0.006),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = W * 0.025 };
        stack.Children.Add(new Viewbox
        {
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = H - (padding * 2) - (text.Footnote is null ? 0 : W * 0.06),
            Child = body,
        });

        if (!string.IsNullOrWhiteSpace(text.Footnote))
        {
            stack.Children.Add(new TextBlock
            {
                Text = text.Footnote.ToUpperInvariant(),
                FontFamily = (FontFamily)Application.Current.Resources["IrisSansFontFamily"],
                FontWeight = FontWeights.SemiBold,
                FontSize = W * 0.022,
                CharacterSpacing = (int)Math.Round(W * 0.003 / (W * 0.022) * 1000),
                Opacity = IrisTheme.Double("IrisFootnoteOpacity"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
            });
        }

        return new Grid { Padding = new Thickness(padding), Children = { stack } };
    }

    private static UIElement BuildImage(ImageContent image) => new Grid
    {
        Children =
        {
            new Rectangle { Fill = IrisTheme.DiagonalGradient(image.Artwork) },
            new Rectangle { Fill = IrisTheme.Glow(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.18, new Point(0.3, 0.25), 0.7, 0.8) },
        },
    };

    private static UIElement BuildMedia(string glyph, Color tint, string title, TimeSpan duration, bool circled)
    {
        var iconSize = W * 0.1;
        UIElement icon = new FontIcon
        {
            Glyph = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IrisIconFontFamily"],
            FontSize = circled ? iconSize * 0.4 : iconSize * 0.7,
            Foreground = new SolidColorBrush(tint),
        };

        if (circled)
        {
            icon = new Grid
            {
                Width = iconSize,
                Height = iconSize,
                Children =
                {
                    new Ellipse
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                        Stroke = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
                        StrokeThickness = 3,
                    },
                    icon,
                },
            };
        }

        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = W * 0.015,
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = title,
                    FontFamily = (FontFamily)Application.Current.Resources["IrisSerifDisplayFontFamily"],
                    FontWeight = FontWeights.Medium,
                    FontSize = W * 0.04,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = W * 0.8,
                    Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
                },
                new TextBlock
                {
                    Text = DurationText.Format(duration),
                    FontFamily = (FontFamily)Application.Current.Resources["IrisSansFontFamily"],
                    FontWeight = FontWeights.SemiBold,
                    FontSize = W * 0.022,
                    Opacity = IrisTheme.Double("IrisFootnoteOpacity"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
                },
            },
        };
    }

    private static UIElement BuildLogo(LogoContent logo) => new StackPanel
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = W * 0.025,
        Children =
        {
            new IrisMark { Width = W * 0.14, Height = W * 0.14 },
            new TextBlock
            {
                Text = logo.Name,
                FontFamily = (FontFamily)Application.Current.Resources["IrisSerifDisplayFontFamily"],
                FontWeight = FontWeights.Medium,
                FontSize = W * 0.04,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
            },
        },
    };
}
