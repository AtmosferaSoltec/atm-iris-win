using System;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Services;

/// <summary>
/// Navigates the root frame of the main window (Login, Register, Shell).
/// </summary>
public interface INavigationService
{
    Frame? Frame { get; set; }

    bool CanGoBack { get; }

    bool NavigateTo(Type pageType, object? parameter = null, bool clearBackStack = false);

    void GoBack();
}
