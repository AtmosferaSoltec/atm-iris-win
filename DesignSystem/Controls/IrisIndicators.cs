using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Iris.DesignSystem.Controls;

internal static class Res
{
    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static FontIcon Icon(string glyph, double size, Brush? foreground = null) => new()
    {
        Glyph = glyph,
        FontFamily = (FontFamily)Application.Current.Resources["IrisIconFontFamily"],
        FontSize = size,
        Foreground = foreground,
    };
}

public enum BannerKind
{
    Error,
    Success,
    Info,
}

/// <summary>Icon + message on a tinted card (IRIS_SPEC §5.7).</summary>
public sealed partial class IrisBanner : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(IrisBanner), new PropertyMetadata(null, (d, _) => ((IrisBanner)d).Apply()));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(BannerKind), typeof(IrisBanner), new PropertyMetadata(BannerKind.Error, (d, _) => ((IrisBanner)d).Apply()));

    private readonly Border _card;
    private readonly FontIcon _icon = Res.Icon(Glyphs.Warning, 16);
    private readonly TextBlock _text = new() { Style = Res.Style("IrisCalloutTextStyle"), VerticalAlignment = VerticalAlignment.Center };

    public IrisBanner()
    {
        IsTabStop = false;
        _text.Foreground = IrisTheme.Brush("IrisTextPrimaryBrush");
        _card = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusMd"],
            BorderThickness = new Thickness(1),
            Padding = (Thickness)Application.Current.Resources["IrisPaddingSm"],
            Child = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
                Children = { _icon, _text },
            },
        };
        Grid.SetColumn(_text, 1);
        Content = _card;
        AutomationProperties.SetLiveSetting(this, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        Apply();
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public BannerKind Kind
    {
        get => (BannerKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Apply()
    {
        var (colorKey, glyph) = Kind switch
        {
            BannerKind.Success => ("IrisSuccessColor", Glyphs.Success),
            BannerKind.Info => ("IrisIndigoColor", Glyphs.Info),
            _ => ("IrisDangerColor", Glyphs.Warning),
        };
        var tint = IrisTheme.Color(colorKey);
        _card.Background = IrisTheme.Tint(tint, 0.12);
        _card.BorderBrush = IrisTheme.Tint(tint, 0.35);
        _icon.Glyph = glyph;
        _icon.Foreground = new SolidColorBrush(tint);
        _text.Text = Message ?? string.Empty;
        Visibility = string.IsNullOrEmpty(Message) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, Message ?? string.Empty);
    }
}

/// <summary>Tinted capsule with optional icon (IRIS_SPEC §5.8).</summary>
public sealed partial class IrisChip : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(IrisChip), new PropertyMetadata(null, (d, _) => ((IrisChip)d).Apply()));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(IrisChip), new PropertyMetadata(null, (d, _) => ((IrisChip)d).Apply()));

    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(IrisChip), new PropertyMetadata(Colors.White, (d, _) => ((IrisChip)d).Apply()));

    private readonly Border _capsule;
    private readonly FontIcon _icon = Res.Icon(string.Empty, 12);
    private readonly TextBlock _text = new() { Style = Res.Style("IrisCaptionTextStyle"), TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };

    public IrisChip()
    {
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        _capsule = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusCapsuleChip"],
            BorderThickness = new Thickness(1),
            Padding = (Thickness)Application.Current.Resources["IrisChipPadding"],
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _icon, _text } },
        };
        Content = _capsule;
        Apply();
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public Color Tint
    {
        get => (Color)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    private void Apply()
    {
        var brush = new SolidColorBrush(Tint);
        _capsule.Background = IrisTheme.Tint(Tint, 0.14);
        _capsule.BorderBrush = IrisTheme.Tint(Tint, 0.25);
        _icon.Foreground = brush;
        _icon.Glyph = Glyph ?? string.Empty;
        _icon.Visibility = string.IsNullOrEmpty(Glyph) ? Visibility.Collapsed : Visibility.Visible;
        _text.Foreground = brush;
        _text.Text = Text ?? string.Empty;
    }
}

/// <summary>UPPERCASE overline with an optional right-hand accessory (IRIS_SPEC §5.9).</summary>
public sealed partial class IrisSectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(IrisSectionHeader), new PropertyMetadata(null, (d, e) => ((IrisSectionHeader)d)._title.Text = ((string?)e.NewValue ?? string.Empty).ToUpperInvariant()));

    public static readonly DependencyProperty AccessoryProperty =
        DependencyProperty.Register(nameof(Accessory), typeof(object), typeof(IrisSectionHeader), new PropertyMetadata(null, (d, e) => ((IrisSectionHeader)d)._accessory.Content = e.NewValue));

    private readonly TextBlock _title = new() { Style = Res.Style("IrisOverlineTextStyle"), VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentPresenter _accessory = new() { VerticalAlignment = VerticalAlignment.Center };

    public IrisSectionHeader()
    {
        IsTabStop = false;
        AutomationProperties.SetHeadingLevel(_title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { _title, _accessory },
        };
        Grid.SetColumn(_accessory, 1);
        Content = grid;
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Accessory
    {
        get => GetValue(AccessoryProperty);
        set => SetValue(AccessoryProperty, value);
    }
}

/// <summary>7px pulsing live dot (IRIS_SPEC §5.10).</summary>
public sealed partial class IrisLiveDot : UserControl
{
    private readonly Ellipse _dot;
    private readonly ScaleTransform _scale = new();
    private Storyboard? _pulse;

    public IrisLiveDot()
    {
        IsTabStop = false;
        var size = IrisTheme.Double("IrisLiveDotSize");
        _dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = IrisTheme.Brush("IrisLiveBrush"),
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = _scale,
        };
        Content = _dot;
        VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => _pulse?.Stop();
    }

    private void Start()
    {
        if (!Motion.AnimationsEnabled)
        {
            return;
        }

        _pulse ??= new Storyboard
        {
            Children =
            {
                Pulse(_dot, nameof(UIElement.Opacity), 0.3),
                Pulse(_scale, nameof(ScaleTransform.ScaleX), 0.8),
                Pulse(_scale, nameof(ScaleTransform.ScaleY), 0.8),
            },
        };
        _pulse.Begin();

        static DoubleAnimation Pulse(DependencyObject target, string property, double to)
        {
            var animation = new DoubleAnimation
            {
                From = 1,
                To = to,
                Duration = TimeSpan.FromSeconds(0.9),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            return animation;
        }
    }
}

/// <summary>Live dot + overline label in a glass capsule ("EN VIVO", "EN PANTALLA", "REPRODUCIENDO").</summary>
public sealed partial class IrisLiveIndicator : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(IrisLiveIndicator), new PropertyMetadata(null, (d, e) =>
        {
            var self = (IrisLiveIndicator)d;
            self._text.Text = (string?)e.NewValue ?? string.Empty;
            AutomationProperties.SetName(self, self._text.Text);
        }));

    private readonly TextBlock _text;

    public IrisLiveIndicator()
    {
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        _text = new TextBlock
        {
            Style = Res.Style("IrisOverlineTextStyle"),
            Foreground = IrisTheme.Brush("IrisTextPrimaryBrush"),
            CharacterSpacing = (int)Application.Current.Resources["IrisTrackingCaps"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Border
        {
            Style = Res.Style("IrisGlassCapsuleStyle"),
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusCapsuleBadge"],
            Padding = new Thickness(10, 5, 12, 5),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new IrisLiveDot(), _text } },
        };
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}

/// <summary>24px selection check (IRIS_SPEC §5.11).</summary>
public sealed partial class IrisCheckmark : UserControl
{
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(IrisCheckmark), new PropertyMetadata(false, (d, _) => ((IrisCheckmark)d).Apply(animated: true)));

    private readonly Grid _on;
    private readonly ScaleTransform _onScale = new();

    public IrisCheckmark()
    {
        IsTabStop = false;
        var size = IrisTheme.Double("IrisCheckmarkSize");
        Width = Height = size;
        var off = new Ellipse
        {
            Fill = IrisTheme.Brush("IrisCheckOffBrush"),
            Stroke = IrisTheme.Brush("IrisStrokeStrongBrush"),
            StrokeThickness = 1.5,
        };
        _on = new Grid
        {
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = _onScale,
            Children =
            {
                new Ellipse { Fill = IrisTheme.Brush("IrisAccentGradientBrush") },
                new FontIcon
                {
                    Glyph = Glyphs.CheckMark,
                    FontFamily = (FontFamily)Application.Current.Resources["IrisIconFontFamily"],
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = IrisTheme.Brush("IrisTextInverseBrush"),
                },
            },
        };
        Content = new Grid { Children = { off, _on } };
        Apply(animated: false);
    }

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    private void Apply(bool animated)
    {
        _on.Opacity = IsChecked ? 1 : 0;
        if (IsChecked && animated && Motion.AnimationsEnabled)
        {
            _onScale.ScaleX = _onScale.ScaleY = 0.5;
            Motion.Animate(_onScale, nameof(ScaleTransform.ScaleX), 1, Motion.Snappy);
            Motion.Animate(_onScale, nameof(ScaleTransform.ScaleY), 1, Motion.Snappy);
        }
    }
}
