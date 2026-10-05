using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Iris.Core.Models;

namespace Iris.Shell;

public enum AppRoute
{
    Home,
    Console,
    Modules,
    Services,
    People,
    Times,
}

/// <summary>What the live console receives when a service starts from Home (IRIS_SPEC §8.2).</summary>
public sealed record ConsoleLaunch(ServiceType? ServiceType, ChurchModules Modules, IReadOnlyList<Person> People, DateTime StartedAt);

/// <summary>
/// Signed-in navigation (IRIS_SPEC §8.2): Home, the console for a service type, and the church
/// screens. Every secondary screen goes back to Home.
/// </summary>
public sealed partial class SignedInNavigator : ObservableObject
{
    [ObservableProperty]
    public partial AppRoute Route { get; private set; }

    public ConsoleLaunch? Launch { get; private set; }

    /// <summary>"Ver en Tiempos" opens this record once.</summary>
    public Guid? PendingRecordId { get; set; }

    public void GoHome() => Go(AppRoute.Home);

    public void Open(AppRoute route) => Go(route);

    public void StartService(ConsoleLaunch launch)
    {
        Launch = launch;
        Go(AppRoute.Console);
    }

    /// <summary>Back to Home without notifying (used on sign out).</summary>
    public void Reset()
    {
        Launch = null;
        PendingRecordId = null;
        Route = AppRoute.Home;
    }

    private void Go(AppRoute route)
    {
        // Re-raise even when the route is the same so the window always shows the request.
        if (Route == route)
        {
            OnPropertyChanged(nameof(Route));
        }
        else
        {
            Route = route;
        }
    }
}
