using System;
using System.Threading.Tasks;
using Iris.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Shell.Platform;

/// <summary><see cref="IDialogService"/> with a <see cref="ContentDialog"/> over the main window.</summary>
public sealed class ContentDialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false, string cancelText = "Cancelar")
    {
        var root = App.MainWindow.Content.XamlRoot;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ElementTheme.Dark,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = destructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        if (destructive)
        {
            dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["IrisDangerButtonStyle"];
        }

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Entendido",
        };
        await dialog.ShowAsync();
    }
}
