using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Services;
using Iris.Views.Auth;

namespace Iris.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public ShellViewModel(INavigationService navigation)
    {
        _navigation = navigation;
    }

    public string UserName { get; } = "Operador";

    public string UserInitials { get; } = "OP";

    [ObservableProperty]
    public partial string CurrentSection { get; set; } = "Inicio";

    [ObservableProperty]
    public partial bool IsProjecting { get; set; }

    [RelayCommand]
    private void SignOut() => _navigation.NavigateTo(typeof(LoginPage), clearBackStack: true);
}
