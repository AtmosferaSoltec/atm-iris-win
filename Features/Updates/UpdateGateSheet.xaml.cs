using Microsoft.UI.Xaml.Controls;

namespace Iris.Features.Updates;

public sealed partial class UpdateGateSheet : UserControl
{
    public UpdateGateSheet()
    {
        InitializeComponent();
    }

    public UpdateGateViewModel ViewModel { get; } = App.GetService<UpdateGateViewModel>();
}
