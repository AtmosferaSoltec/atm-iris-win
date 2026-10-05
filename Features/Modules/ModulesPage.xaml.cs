using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Iris.Features.Modules;

public sealed partial class ModulesPage : Page
{
    public ModulesPage()
    {
        InitializeComponent();
    }

    public ModulesViewModel ViewModel { get; } = App.GetService<ModulesViewModel>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
    }
}
