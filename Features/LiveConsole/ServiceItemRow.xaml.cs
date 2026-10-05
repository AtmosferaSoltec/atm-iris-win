using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Iris.Features.LiveConsole;

public sealed partial class ServiceItemRow : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(ServiceItemViewModel), typeof(ServiceItemRow), new PropertyMetadata(null));

    public ServiceItemRow()
    {
        InitializeComponent();
    }

    public ServiceItemViewModel? Item
    {
        get => (ServiceItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    // Hover: subtle fill + trash button (desktop stand-in for swipe-to-delete).
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    private void SetHover(bool isHovering)
    {
        TrashButton.Visibility = isHovering ? Visibility.Visible : Visibility.Collapsed;
        HoverFill.Visibility = isHovering && Item?.IsSelected != true ? Visibility.Visible : Visibility.Collapsed;
    }
}
