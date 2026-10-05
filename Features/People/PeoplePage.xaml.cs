using System;
using Iris.Core.Persistence;
using Iris.Shell;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Iris.Features.People;

public sealed partial class PeoplePage : Page
{
    public PeoplePage()
    {
        InitializeComponent();
        ViewModel.NameFieldFocusRequested += OnNameFieldFocusRequested;
        Unloaded += (_, _) => ViewModel.NameFieldFocusRequested -= OnNameFieldFocusRequested;
    }

    public PeopleViewModel ViewModel { get; } = App.GetService<PeopleViewModel>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
        _watch = App.GetService<DataWatcher>().Watch(() => ViewModel.LoadCommand.Execute(null), StoreChangeKind.People, StoreChangeKind.ServiceRecords);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _watch?.Dispose();
    }

    private IDisposable? _watch;

    // Enter adds and keeps the focus in the field.
    private void OnNewNameSubmitted(object? sender, EventArgs e) => ViewModel.AddCommand.Execute(null);

    private void OnRenameSubmitted(object? sender, EventArgs e) => ViewModel.ConfirmRenameCommand.Execute(null);

    private void OnNameFieldFocusRequested(object? sender, EventArgs e) => NewNameField.FocusInput();
}
