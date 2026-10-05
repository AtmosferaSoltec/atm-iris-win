using Iris.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Views.Auth;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; } = App.GetService<LoginViewModel>();

    public LoginPage()
    {
        InitializeComponent();
    }
}
