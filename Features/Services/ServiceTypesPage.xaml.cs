using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Iris.Features.Services;

public sealed partial class ServiceTypesPage : Page
{
    public ServiceTypesPage()
    {
        InitializeComponent();
    }

    public ServiceTypesViewModel ViewModel { get; } = App.GetService<ServiceTypesViewModel>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
    }
}
