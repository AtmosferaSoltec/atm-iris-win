using System.ComponentModel;
using Iris.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Iris.Features.Home;

public sealed partial class HomePage : Page
{
    public HomePage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        LayoutTiles();
    }

    public HomeViewModel ViewModel { get; } = App.GetService<SessionScope>().Home;

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.AppearCommand.Execute(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(HomeViewModel.Modules))
        {
            LayoutTiles();
        }
    }

    // Row 1: Servicios · Biblioteca; row 2: Tiempos · Personas · Módulos. Without time control: one row of three.
    private void LayoutTiles()
    {
        if (ViewModel.ShowsTimeControl)
        {
            Place(ServicesTile, 0, 0, 3);
            Place(LibraryTile, 0, 3, 3);
            Place(TimesTile, 1, 0, 2);
            Place(PeopleTile, 1, 2, 2);
            Place(ModulesTile, 1, 4, 2);
        }
        else
        {
            Place(ServicesTile, 0, 0, 2);
            Place(LibraryTile, 0, 2, 2);
            Place(ModulesTile, 0, 4, 2);
        }

        static void Place(FrameworkElement tile, int row, int column, int span)
        {
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            Grid.SetColumnSpan(tile, span);
        }
    }
}
