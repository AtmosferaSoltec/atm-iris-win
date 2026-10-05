using Microsoft.UI.Xaml.Controls;

namespace Iris.Features.Connection;

public sealed partial class ConnectionSheet : UserControl
{
    public ConnectionSheet()
    {
        InitializeComponent();
    }

    public ConnectionViewModel ViewModel { get; } = App.GetService<ConnectionViewModel>();
}
