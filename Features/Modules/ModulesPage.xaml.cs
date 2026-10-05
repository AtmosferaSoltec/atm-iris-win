using System;
using Iris.Core.Persistence;
using Iris.Shell;
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
        _watch = App.GetService<DataWatcher>().Watch(() => ViewModel.LoadCommand.Execute(null), StoreChangeKind.Church);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _watch?.Dispose();
    }

    private IDisposable? _watch;
}
