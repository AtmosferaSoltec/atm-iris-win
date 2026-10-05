using Iris.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Views.Auth;

public sealed partial class RegisterPage : Page
{
    public RegisterViewModel ViewModel { get; } = App.GetService<RegisterViewModel>();

    public RegisterPage()
    {
        InitializeComponent();
    }
}
