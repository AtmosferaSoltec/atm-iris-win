using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Iris.DesignSystem.Controls;

/// <summary>
/// In-window modal sheet (the iPad ".form" / ".page" sheets): dimmed scrim + canvasElevated panel.
/// Esc or clicking the scrim runs <see cref="CloseCommand"/>; focus cycles inside the panel.
/// </summary>
[ContentProperty(Name = nameof(ModalContent))]
public sealed partial class IrisModal : UserControl
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(IrisModal), new PropertyMetadata(false, (d, _) => ((IrisModal)d).ApplyOpen()));

    public static readonly DependencyProperty ModalContentProperty =
        DependencyProperty.Register(nameof(ModalContent), typeof(object), typeof(IrisModal), new PropertyMetadata(null, (d, e) => ((IrisModal)d)._presenter.Content = e.NewValue));

    public static readonly DependencyProperty PanelWidthProperty =
        DependencyProperty.Register(nameof(PanelWidth), typeof(double), typeof(IrisModal), new PropertyMetadata(620.0, (d, e) => ((IrisModal)d)._panel.Width = (double)e.NewValue));

    public static readonly DependencyProperty PanelHeightProperty =
        DependencyProperty.Register(nameof(PanelHeight), typeof(double), typeof(IrisModal), new PropertyMetadata(double.NaN, (d, e) => ((IrisModal)d)._panel.Height = (double)e.NewValue));

    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(nameof(CloseCommand), typeof(ICommand), typeof(IrisModal), new PropertyMetadata(null));

    private readonly Grid _root;
    private readonly Border _panel;
    private readonly ContentPresenter _presenter = new();

    public IrisModal()
    {
        IsTabStop = false;
        Visibility = Visibility.Collapsed;
        var scrim = new Rectangle { Fill = IrisTheme.Brush("IrisScrimBrush") };
        scrim.Tapped += (_, _) => Close();

        _panel = new Border
        {
            Width = PanelWidth,
            MaxHeight = 860,
            Margin = new Thickness(IrisTheme.Double("IrisSpacingLg")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = IrisTheme.Brush("IrisCanvasElevatedBrush"),
            BorderBrush = IrisTheme.Brush("IrisSurfaceBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["IrisRadiusXl"],
            TabFocusNavigation = KeyboardNavigationMode.Cycle,
            XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled,
            Child = _presenter,
        };

        _root = new Grid { Children = { scrim, _panel } };
        _root.AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), handledEventsToo: true);
        Content = _root;
    }

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    public object? ModalContent { get => GetValue(ModalContentProperty); set => SetValue(ModalContentProperty, value); }

    public double PanelWidth { get => (double)GetValue(PanelWidthProperty); set => SetValue(PanelWidthProperty, value); }

    public double PanelHeight { get => (double)GetValue(PanelHeightProperty); set => SetValue(PanelHeightProperty, value); }

    public ICommand? CloseCommand { get => (ICommand?)GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }

    private void Close()
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void ApplyOpen()
    {
        if (IsOpen)
        {
            Visibility = Visibility.Visible;
            _root.Opacity = 0;
            Motion.Fade(_root, 1, Motion.Snappy);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (FocusManager.FindFirstFocusableElement(_panel) is Control first)
                {
                    first.Focus(FocusState.Programmatic);
                }
            });
        }
        else
        {
            Motion.Fade(_root, 0, Motion.Snappy, () =>
            {
                if (!IsOpen)
                {
                    Visibility = Visibility.Collapsed;
                }
            });
        }
    }
}
