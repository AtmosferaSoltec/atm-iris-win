using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Iris.DesignSystem.Controls;

/// <summary>
/// Proportional block timeline (IRIS_SPEC §6.1b): one capsule per block, width ∝ minutes,
/// colored with the spectrum rotation.
/// </summary>
public sealed partial class IrisBlockTimeline : UserControl
{
    public static readonly DependencyProperty MinutesProperty =
        DependencyProperty.Register(nameof(Minutes), typeof(IReadOnlyList<int>), typeof(IrisBlockTimeline), new PropertyMetadata(null, (d, _) => ((IrisBlockTimeline)d).Rebuild()));

    private readonly Grid _track = new() { ColumnSpacing = 4 };

    public IrisBlockTimeline()
    {
        IsTabStop = false;
        Height = 8;
        Content = _track;
    }

    public IReadOnlyList<int>? Minutes
    {
        get => (IReadOnlyList<int>?)GetValue(MinutesProperty);
        set => SetValue(MinutesProperty, value);
    }

    private void Rebuild()
    {
        _track.Children.Clear();
        _track.ColumnDefinitions.Clear();
        var minutes = Minutes ?? [];
        for (var i = 0; i < minutes.Count; i++)
        {
            _track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, minutes[i]), GridUnitType.Star) });
            var capsule = new Border { Background = Spectrum.Brush(i), CornerRadius = new CornerRadius(4) };
            Grid.SetColumn(capsule, i);
            _track.Children.Add(capsule);
        }
    }
}

/// <summary>Circle with initials, colored by its spectrum index.</summary>
public sealed partial class IrisAvatar : UserControl
{
    public static readonly DependencyProperty InitialsProperty =
        DependencyProperty.Register(nameof(Initials), typeof(string), typeof(IrisAvatar), new PropertyMetadata(null, (d, _) => ((IrisAvatar)d).Apply()));

    public static readonly DependencyProperty ColorIndexProperty =
        DependencyProperty.Register(nameof(ColorIndex), typeof(int), typeof(IrisAvatar), new PropertyMetadata(0, (d, _) => ((IrisAvatar)d).Apply()));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(IrisAvatar), new PropertyMetadata(40.0, (d, _) => ((IrisAvatar)d).Apply()));

    private readonly Ellipse _circle = new() { StrokeThickness = 2, Stroke = IrisTheme.Brush("IrisCanvasElevatedBrush") };
    private readonly TextBlock _text = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = IrisTheme.Brush("IrisTextInverseBrush"),
        FontFamily = (FontFamily)Application.Current.Resources["IrisSansFontFamily"],
    };

    public IrisAvatar()
    {
        IsTabStop = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Content = new Grid { Children = { _circle, _text } };
        Apply();
    }

    public string? Initials { get => (string?)GetValue(InitialsProperty); set => SetValue(InitialsProperty, value); }

    public int ColorIndex { get => (int)GetValue(ColorIndexProperty); set => SetValue(ColorIndexProperty, value); }

    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private void Apply()
    {
        Width = Height = Size;
        _circle.Fill = Spectrum.Brush(ColorIndex);
        _text.Text = Initials ?? string.Empty;
        _text.FontSize = Math.Round(Size * 0.36);
    }
}

/// <summary>
/// Home tile (IRIS_SPEC §6.1b): tinted icon, title, subtitle, optional content and "›".
/// Clicking it runs <see cref="Command"/>.
/// </summary>
[ContentProperty(Name = nameof(TileContent))]
public sealed partial class IrisTile : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(IrisTile), new PropertyMetadata(null, (d, _) => ((IrisTile)d).Apply()));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(IrisTile), new PropertyMetadata(null, (d, _) => ((IrisTile)d).Apply()));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(IrisTile), new PropertyMetadata(null, (d, _) => ((IrisTile)d).Apply()));

    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(IrisTile), new PropertyMetadata(Microsoft.UI.Colors.White, (d, _) => ((IrisTile)d).Apply()));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(IrisTile), new PropertyMetadata(null, (d, e) => ((IrisTile)d)._button.Command = (ICommand?)e.NewValue));

    public static readonly DependencyProperty TileContentProperty =
        DependencyProperty.Register(nameof(TileContent), typeof(object), typeof(IrisTile), new PropertyMetadata(null, (d, e) =>
        {
            var self = (IrisTile)d;
            self._content.Content = e.NewValue;
            self._content.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
        }));

    private readonly Button _button;
    private readonly Border _iconBox;
    private readonly FontIcon _icon = Res.Icon(string.Empty, 18);
    private readonly TextBlock _title = new() { Style = Res.Style("IrisBodyEmphasizedTextStyle") };
    private readonly TextBlock _subtitle = new() { Style = Res.Style("IrisCaptionTextStyle") };
    private readonly ContentPresenter _content = new() { Visibility = Visibility.Collapsed, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    public IrisTile()
    {
        IsTabStop = false;
        _iconBox = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusSm"],
            Child = _icon,
        };
        var chevron = Res.Icon("", 12, IrisTheme.Brush("IrisTextTertiaryBrush"));
        chevron.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid
        {
            ColumnSpacing = IrisTheme.Double("IrisSpacingSm"),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
        };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2, Children = { _title, _subtitle } };
        Grid.SetColumn(titles, 1);
        Grid.SetColumn(chevron, 2);
        header.Children.Add(_iconBox);
        header.Children.Add(titles);
        header.Children.Add(chevron);

        var body = new StackPanel { Spacing = IrisTheme.Double("IrisSpacingMd"), Children = { header, _content } };
        _button = new Button
        {
            Style = Res.Style("IrisPressableButtonStyle"),
            VerticalAlignment = VerticalAlignment.Stretch,
            Content = new Border
            {
                Style = Res.Style("IrisConsoleSurfaceStyle"),
                Padding = (Thickness)Application.Current.Resources["IrisPaddingLg"],
                Child = body,
            },
        };
        Content = _button;
        Apply();
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    public object? TileContent { get => GetValue(TileContentProperty); set => SetValue(TileContentProperty, value); }

    private void Apply()
    {
        _title.Text = Title ?? string.Empty;
        _subtitle.Text = Subtitle ?? string.Empty;
        _icon.Glyph = Glyph ?? string.Empty;
        _icon.Foreground = new SolidColorBrush(Tint);
        _iconBox.Background = IrisTheme.Tint(Tint, 0.14);
        AutomationProperties.SetName(_button, $"{Title}. {Subtitle}");
    }
}

/// <summary>
/// Actual vs planned bar on a shared scale (IRIS_SPEC §6.10): the planned part in the success
/// tint, the overtime past it in danger. <see cref="MaxValue"/> is the largest value in the list (shared scale).
/// </summary>
public sealed partial class IrisTimeBar : UserControl
{
    public static readonly DependencyProperty ActualProperty = Register(nameof(Actual));
    public static readonly DependencyProperty PlannedProperty = Register(nameof(Planned));
    public static readonly DependencyProperty MaxValueProperty = Register(nameof(MaxValue));

    private readonly Grid _track = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };
    private readonly Border _over = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(0, 3, 3, 0) };
    private readonly Rectangle _plannedMark = new() { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, -3, 0, -3) };

    public IrisTimeBar()
    {
        IsTabStop = false;
        Height = 6;
        _track.Background = IrisTheme.Brush("IrisSurfaceRaisedBrush");
        _track.CornerRadius = new CornerRadius(3);
        _fill.Background = IrisTheme.Brush("IrisSuccessBrush");
        _over.Background = IrisTheme.Brush("IrisDangerBrush");
        _plannedMark.Fill = IrisTheme.Brush("IrisTextSecondaryBrush");
        Content = new Grid { Children = { _track, _fill, _over, _plannedMark } };
        SizeChanged += (_, _) => Layout();
    }

    public double Actual { get => (double)GetValue(ActualProperty); set => SetValue(ActualProperty, value); }

    public double Planned { get => (double)GetValue(PlannedProperty); set => SetValue(PlannedProperty, value); }

    public double MaxValue { get => (double)GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(double), typeof(IrisTimeBar), new PropertyMetadata(0.0, (d, _) => ((IrisTimeBar)d).Layout()));

    private void Layout()
    {
        var width = ActualWidth;
        var scale = Math.Max(1, Math.Max(MaxValue, Math.Max(Actual, Planned)));
        if (width <= 0)
        {
            return;
        }

        var actual = width * Actual / scale;
        var planned = width * Planned / scale;
        _fill.Width = Math.Min(actual, planned);
        _over.Margin = new Thickness(planned, 0, 0, 0);
        _over.Width = Math.Max(0, actual - planned);
        _plannedMark.Margin = new Thickness(Math.Max(0, planned - 1), -3, 0, -3);
    }
}
