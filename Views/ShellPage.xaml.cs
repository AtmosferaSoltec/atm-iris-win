using Iris.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Views;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel { get; } = App.GetService<ShellViewModel>();

    public ShellPage()
    {
        InitializeComponent();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            ViewModel.CurrentSection = item.Content?.ToString() ?? string.Empty;
        }
    }
}
