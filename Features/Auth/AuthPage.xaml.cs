using System;
using Iris.DesignSystem;
using Iris.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Features.Auth;

public sealed partial class AuthPage : Page
{
    public AuthPage()
    {
        InitializeComponent();
        ViewModel.FocusRequested += OnFocusRequested;
        Loaded += (_, _) =>
        {
            App.MainWindow.ConfigureTitleBar(DragStrip);
            ViewModel.RunShowcase();
        };
        Unloaded += (_, _) => ViewModel.StopShowcase();
    }

    public AuthViewModel ViewModel { get; } = App.GetService<AuthViewModel>();

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Wide layout fills the window so the hero and the panel can center vertically.
        var isWide = e.NewSize.Width >= IrisTheme.Double("IrisAuthWideBreakpoint");
        LayoutRoot.MinHeight = isWide ? e.NewSize.Height : 0;
    }

    // Enter moves to the next visible field; on the last one it submits.
    private void OnFieldSubmitted(object? sender, EventArgs e)
    {
        IrisTextField? next =
            ReferenceEquals(sender, ChurchField) ? LeaderField
            : ReferenceEquals(sender, LeaderField) ? EmailField
            : ReferenceEquals(sender, EmailField) ? PasswordField
            : null;

        if (next is not null)
        {
            next.FocusInput();
        }
        else if (ViewModel.SubmitCommand.CanExecute(null))
        {
            ViewModel.SubmitCommand.Execute(null);
        }
    }

    private void OnRecoverySubmitted(object? sender, EventArgs e) => ViewModel.Recovery?.SendCommand.Execute(null);

    private void OnFocusRequested(object? sender, AuthField field) => (field switch
    {
        AuthField.ChurchName => ChurchField,
        AuthField.LeaderName => LeaderField,
        AuthField.Email => EmailField,
        _ => PasswordField,
    }).FocusInput();
}
