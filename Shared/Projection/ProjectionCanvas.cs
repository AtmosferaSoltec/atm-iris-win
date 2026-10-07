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

/// <summary>Which player a canvas shows when its frame is a playing video.</summary>
public enum VideoSurfaceRole
{
    /// <summary>No player: a video draws its title and duration (stage, thumbnails).</summary>
    None,

    /// <summary>The TV window: the main player.</summary>
    Tv,

    /// <summary>The EN VIVO preview of the console: the muted mirror player.</summary>
    Preview,
}

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
    /// <summary>The TV window and the console preview show the real video; everything else shows its marker.</summary>
    public static readonly DependencyProperty VideoRoleProperty =
        DependencyProperty.Register(nameof(VideoRole), typeof(VideoSurfaceRole), typeof(ProjectionCanvas), new PropertyMetadata(VideoSurfaceRole.None));

    /// <summary>
    /// How the lyrics look here. Null (everywhere but the Proyección preview) follows
    /// <see cref="ProjectionTypography.Current"/>, so thumbnails, EN VIVO and the TV always match.
    /// </summary>
    public static readonly DependencyProperty TypographyProperty =
        DependencyProperty.Register(nameof(Typography), typeof(ProjectionSettings), typeof(ProjectionCanvas), new PropertyMetadata(null, (d, _) => ((ProjectionCanvas)d).Rerender()));

    public static readonly DependencyProperty UsesTransitionsProperty =
        DependencyProperty.Register(nameof(UsesTransitions), typeof(bool), typeof(ProjectionCanvas), new PropertyMetadata(false));

    /// <summary>
    /// Thumbnails: decode pictures at this width (px) and use shell thumbnails for videos. 0 = full size (TV and live preview).
    /// </summary>
    public static readonly DependencyProperty DecodePixelWidthProperty =
        DependencyProperty.Register(nameof(DecodePixelWidth), typeof(int), typeof(ProjectionCanvas), new PropertyMetadata(0, (d, _) =>
        {
            var canvas = (ProjectionCanvas)d;
            if (canvas.Frame is not null)
            {
                canvas.Render(null);
            }
        }));

    private readonly Grid _stage = new() { Width = W, Height = H };

    public ProjectionCanvas()
    {
        IsTabStop = false;
        Content = new Grid
        {
            Background = IrisTheme.Brush("IrisBlackBrush"),
            Children = { new Viewbox { Stretch = Stretch.Uniform, Child = _stage } },
        };
        Loaded += (_, _) =>
        {
            ProjectionTypography.Changed -= OnTypographyChanged;
            ProjectionTypography.Changed += OnTypographyChanged;
        };
        Unloaded += (_, _) => ProjectionTypography.Changed -= OnTypographyChanged;
    }

    public ProjectionSettings? Typography
    {
        get => (ProjectionSettings?)GetValue(TypographyProperty);
        set => SetValue(TypographyProperty, value);
    }

    private ProjectionSettings EffectiveTypography => Typography ?? ProjectionTypography.Current;

    private void OnTypographyChanged(object? sender, EventArgs e)
    {
        if (Typography is null)
        {
            Rerender();
        }
    }

    /// <summary>Draws the same frame again (the typography changed, not the frame).</summary>
    private void Rerender()
    {
        if (Frame is not null && _stage.Children.Count > 0)
        {
            _stage.Children.Clear();
            Render(null);
        }
    }

    public ProjectionFrame? Frame
    {
        get => (ProjectionFrame?)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    public VideoSurfaceRole VideoRole
    {
        get => (VideoSurfaceRole)GetValue(VideoRoleProperty);
        set => SetValue(VideoRoleProperty, value);
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

    private Grid BuildLayer(ProjectionFrame frame)
    {
        var layer = new Grid { Width = W, Height = H, Background = IrisTheme.Brush("IrisBlackBrush") };

        if (frame.Background is { } background)
        {
            layer.Children.Add(new Rectangle { Fill = IrisTheme.DiagonalGradient(background.Colors) });
            if (background.ImagePath is { } picture && MediaImage(picture, Stretch.UniformToFill) is { } backdrop)
            {
                layer.Children.Add(backdrop);
            }

            layer.Children.Add(new Rectangle { Fill = IrisTheme.Glow(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.14, new Point(0.5, 0), 0.9, 0.7) });
            layer.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)) });
        }

        var content = frame.Content switch
        {
            TextContent text => BuildText(text, EffectiveTypography),
            ImageContent image => BuildImage(image),
            VideoContent video => BuildVideo(video),
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

    // Letra / versículo (api-contract §6): the church's typeface at its size (points on a 1920-wide screen, scaled to
    // the stage), Medium, centered, shrinks to fit; footnote in the same typeface at 0.022/0.046 of the body, uppercase at 60%.
    private static UIElement BuildText(TextContent text, ProjectionSettings typography)
    {
        const double padding = W * 0.07;
        var bodySize = W / 1920 * typography.FontSizePt;
        var font = ProjectionFonts.FontFamily(typography.FontFamily);
        var body = new TextBlock
        {
            Text = text.Body,
            Width = W - (padding * 2),
            FontFamily = font,
            FontWeight = FontWeights.Medium,
            FontSize = bodySize,
            LineHeight = (bodySize * 1.2) + (W * 0.006),
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
        };

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = W * 0.025 };
        stack.Children.Add(new Viewbox
        {
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = H - (padding * 2) - (text.Footnote is null ? 0 : bodySize * 1.3),
            Child = body,
        });

        if (!string.IsNullOrWhiteSpace(text.Footnote))
        {
            stack.Children.Add(new TextBlock
            {
                Text = text.Footnote.ToUpperInvariant(),
                FontFamily = font,
                FontWeight = FontWeights.SemiBold,
                FontSize = bodySize * (0.022 / 0.046),
                CharacterSpacing = (int)Math.Round(W * 0.003 / (bodySize * (0.022 / 0.046)) * 1000),
                Opacity = IrisTheme.Double("IrisFootnoteOpacity"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
            });
        }

        return new Grid { Padding = new Thickness(padding), Children = { stack } };
    }

    // A real picture fills the stage (Uniform, on black); design data without a file keeps the gradient marker.
    private UIElement BuildImage(ImageContent image)
    {
        if (image.LocalPath is { } path && MediaImage(path, Stretch.Uniform) is { } picture)
        {
            return new Grid { Background = IrisTheme.Brush("IrisBlackBrush"), Children = { picture } };
        }

        return new Grid
        {
            Children =
            {
                new Rectangle { Fill = IrisTheme.DiagonalGradient(image.Artwork) },
                new Rectangle { Fill = IrisTheme.Glow(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.18, new Point(0.3, 0.25), 0.7, 0.8) },
            },
        };
    }

    // Videos: in thumbnails the first frame (shell thumbnail) behind the play mark; the player itself arrives with the TV phase.
    private UIElement BuildVideo(VideoContent video)
    {
        if (DecodePixelWidth <= 0)
        {
            if (VideoRole != VideoSurfaceRole.None && video.LocalPath is not null && App.Services.GetService(typeof(Iris.Core.Services.IMediaPlaybackService)) is Iris.Shell.Platform.LiveMediaPlaybackService service)
            {
                var element = new MediaPlayerElement { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform };
                element.SetMediaPlayer(VideoRole == VideoSurfaceRole.Tv ? service.Main : service.Preview);
                return new Grid { Background = IrisTheme.Brush("IrisBlackBrush"), Children = { element } };
            }

            return BuildMedia(Glyphs.Play, IrisTheme.Color("IrisTextPrimaryColor"), video.Title, video.Duration, circled: true);
        }

        // Thumbnail: gradient with the first frame over it; the tile draws the play mark and the duration.
        var thumbnail = new Grid { Children = { new Rectangle { Fill = IrisTheme.DiagonalGradient(["#2A1658", "#4E5BFF"]) } } };
        if (video.LocalPath is { } path)
        {
            var frame = new Image { Stretch = Stretch.UniformToFill };
            thumbnail.Children.Add(frame);
            _ = LoadVideoThumbnailAsync(frame, path);
        }

        return thumbnail;
    }

    private async System.Threading.Tasks.Task LoadVideoThumbnailAsync(Image target, string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView, (uint)DecodePixelWidth);
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            target.Source = bitmap;
        }
        catch (Exception)
        {
            // No shell thumbnail (codec missing, file gone): the gradient and play mark stay.
        }
    }

    /// <summary>A picture from disk, decoded at reduced size in thumbnails; hides itself if the file cannot be read.</summary>
    private Image? MediaImage(string path, Stretch stretch)
    {
        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        if (DecodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = DecodePixelWidth;
        }

        bitmap.UriSource = new Uri(path);
        var image = new Image { Source = bitmap, Stretch = stretch };
        image.ImageFailed += (_, _) => image.Visibility = Visibility.Collapsed;
        return image;
    }

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
