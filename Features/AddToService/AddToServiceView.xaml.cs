using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Features.AddToService;

public sealed partial class AddToServiceView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(AddToServiceViewModel), typeof(AddToServiceView), new PropertyMetadata(null));

    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(nameof(CloseCommand), typeof(ICommand), typeof(AddToServiceView), new PropertyMetadata(null));

    public AddToServiceView()
    {
        InitializeComponent();
    }

    public AddToServiceViewModel? ViewModel
    {
        get => (AddToServiceViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
}
