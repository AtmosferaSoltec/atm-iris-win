using System;
using Iris.Core.Persistence;
using Iris.Shell;
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
        _watch = App.GetService<DataWatcher>().Watch(() => ViewModel.LoadCommand.Execute(null), StoreChangeKind.ServiceTypes, StoreChangeKind.People, StoreChangeKind.Church);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _watch?.Dispose();
    }

    private IDisposable? _watch;
}
