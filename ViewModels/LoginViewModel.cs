using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Services;
using Iris.Views;
using Iris.Views.Auth;

namespace Iris.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public LoginViewModel(INavigationService navigation)
    {
        _navigation = navigation;
    }

    [ObservableProperty]
    public partial string UserName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RememberMe { get; set; }

    // Layout only: no validation, goes straight to the shell.
    [RelayCommand]
    private void SignIn() => _navigation.NavigateTo(typeof(ShellPage), clearBackStack: true);

    [RelayCommand]
    private void GoToRegister() => _navigation.NavigateTo(typeof(RegisterPage));
}
