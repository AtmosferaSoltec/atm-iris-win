using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Iris.Services;

public sealed class NavigationService : INavigationService
{
    public Frame? Frame { get; set; }

    public bool CanGoBack => Frame?.CanGoBack ?? false;

    public bool NavigateTo(Type pageType, object? parameter = null, bool clearBackStack = false)
    {
        if (Frame is null || Frame.Content?.GetType() == pageType)
        {
            return false;
        }

        var navigated = Frame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
        if (navigated && clearBackStack)
        {
            Frame.BackStack.Clear();
        }

        return navigated;
    }

    public void GoBack()
    {
        if (CanGoBack)
        {
            Frame!.GoBack();
        }
    }
}
