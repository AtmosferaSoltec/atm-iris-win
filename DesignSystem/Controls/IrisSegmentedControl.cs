using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Iris.DesignSystem.Controls;

/// <summary>
/// Capsule segmented control (IRIS_SPEC §5.5): the selected segment is a warm-white capsule
/// with inverse text that slides to the new position.
/// </summary>
public sealed partial class IrisSegmentedControl : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IList<string>), typeof(IrisSegmentedControl), new PropertyMetadata(null, (d, _) => ((IrisSegmentedControl)d).Rebuild()));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(IrisSegmentedControl), new PropertyMetadata(0, (d, _) => ((IrisSegmentedControl)d).ApplySelection(animated: true)));

    private readonly Grid _track = new();
    private readonly Border _thumb;
    private readonly TranslateTransform _thumbOffset = new();
    private readonly List<Button> _segments = [];

    public IrisSegmentedControl()
    {
        IsTabStop = false;
        _thumb = new Border
        {
            Background = IrisTheme.Brush("IrisTextPrimaryBrush"),
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusCapsuleSegment"],
            RenderTransform = _thumbOffset,
        };
        Content = new Border
        {
            Background = IrisTheme.Brush("IrisSurfaceBrush"),
            BorderBrush = IrisTheme.Brush("IrisStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusCapsuleTrack"],
            Padding = new Thickness(IrisTheme.Double("IrisSpacingXxs")),
            Child = _track,
        };
        _track.SizeChanged += (_, _) => ApplySelection(animated: false);
    }

    public IList<string>? Items
    {
        get => (IList<string>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    private void Rebuild()
    {
        _track.Children.Clear();
        _track.ColumnDefinitions.Clear();
        _segments.Clear();
        var items = Items ?? [];
        foreach (var _ in items)
        {
            _track.ColumnDefinitions.Add(new ColumnDefinition());
        }

        _track.Children.Add(_thumb);
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var segment = new Button
            {
                Content = items[i],
                Template = (ControlTemplate)Application.Current.Resources["IrisChromeButtonTemplate"],
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusCapsuleSegment"],
                Height = IrisTheme.Double("IrisSegmentHeight"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)Application.Current.Resources["IrisSansFontFamily"],
                FontSize = IrisTheme.Double("IrisCalloutFontSize"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                UseSystemFocusVisuals = true,
            };
            segment.Click += (_, _) => SelectedIndex = index;
            Grid.SetColumn(segment, i);
            _segments.Add(segment);
            _track.Children.Add(segment);
        }

        ApplySelection(animated: false);
    }

    private void ApplySelection(bool animated)
    {
        if (_segments.Count == 0)
        {
            return;
        }

        var index = System.Math.Clamp(SelectedIndex, 0, _segments.Count - 1);
        for (var i = 0; i < _segments.Count; i++)
        {
            var selected = i == index;
            _segments[i].Foreground = IrisTheme.Brush(selected ? "IrisTextInverseBrush" : "IrisTextSecondaryBrush");
            AutomationProperties.SetItemStatus(_segments[i], selected ? "Seleccionado" : string.Empty);
        }

        var offset = index * (_track.ActualWidth / _segments.Count);
        if (animated && _track.ActualWidth > 0)
        {
            Motion.Animate(_thumbOffset, nameof(TranslateTransform.X), offset, Motion.Snappy);
        }
        else
        {
            _thumbOffset.X = offset;
        }
    }
}
