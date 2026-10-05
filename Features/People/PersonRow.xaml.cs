using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Iris.Features.People;

public sealed partial class PersonRow : UserControl
{
    public static readonly DependencyProperty RowProperty =
        DependencyProperty.Register(nameof(Row), typeof(PersonRowViewModel), typeof(PersonRow), new PropertyMetadata(null));

    public PersonRow()
    {
        InitializeComponent();
    }

    public PersonRowViewModel? Row
    {
        get => (PersonRowViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    // Hover shows rename/trash (desktop stand-in for swipe and long press). Opacity keeps the row height stable.
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => HoverActions.Opacity = 1;

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => HoverActions.Opacity = 0;
}
