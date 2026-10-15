using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private IDisposable? _watch;

    public HomeViewModel ViewModel { get; } = App.GetService<SessionScope>().Home;

    public SyncIndicatorViewModel Sync { get; } = App.GetService<SyncIndicatorViewModel>();

    public Visibility AnyVisible(bool first, bool second) => first || second ? Visibility.Visible : Visibility.Collapsed;

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.AppearCommand.Execute(null);
        ViewModel.Activate();
        ShowWelcomeOnce();
        _ = App.GetService<Iris.Features.Updates.UpdateGateViewModel>().CheckAsync();
        _watch = App.GetService<DataWatcher>().Watch(() => ViewModel.AppearCommand.Execute(null));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Deactivate();
        _watch?.Dispose();
    }

    // The welcome is shown once per person on this computer.
    private void ShowWelcomeOnce()
    {
        if (App.GetService<SessionStore>().Session?.UserId is not { } userId || App.Settings.HasSeenWelcome(userId))
        {
            return;
        }

        WelcomeModal.CloseCommand = new RelayCommand(DismissWelcome);
        WelcomeModal.IsOpen = true;
    }

    private void DismissWelcome()
    {
        if (App.GetService<SessionStore>().Session?.UserId is { } userId)
        {
            App.Settings.MarkWelcomeSeen(userId);
        }

        WelcomeModal.IsOpen = false;
    }

    private async void OnOpenWebClick(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri(Iris.Core.WebLinks.App));

    private void OnWelcomeDismissed(object sender, RoutedEventArgs e) => DismissWelcome();

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
