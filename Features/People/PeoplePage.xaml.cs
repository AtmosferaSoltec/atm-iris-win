using System;
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
    }

    // Enter adds and keeps the focus in the field.
    private void OnNewNameSubmitted(object? sender, EventArgs e) => ViewModel.AddCommand.Execute(null);

    private void OnRenameSubmitted(object? sender, EventArgs e) => ViewModel.ConfirmRenameCommand.Execute(null);

    private void OnNameFieldFocusRequested(object? sender, EventArgs e) => NewNameField.FocusInput();
}
