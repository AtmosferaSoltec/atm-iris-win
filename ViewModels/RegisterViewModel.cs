using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Services;

namespace Iris.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly INavigationService _navigation;

    public RegisterViewModel(INavigationService navigation)
    {
        _navigation = navigation;
    }

    public IReadOnlyList<string> Roles { get; } = ["Administrador", "Operador"];

    [ObservableProperty]
    public partial string FullName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedRoleIndex { get; set; } = 1;

    // Layout only: nothing is saved, returns to the login screen.
    [RelayCommand]
    private void CreateAccount() => _navigation.GoBack();

    [RelayCommand]
    private void GoToLogin() => _navigation.GoBack();
}
