using System;
using Iris.Core.Services;
using Iris.Core.Services.Mocks;
using Iris.Features.Auth;
using Iris.Features.Home;
using Iris.Features.LiveConsole;
using Iris.Features.Modules;
using Iris.Features.People;
using Iris.Features.Services;
using Iris.Features.Times;
using Microsoft.Extensions.DependencyInjection;

namespace Iris.Shell;

/// <summary>
/// Composition root. <see cref="Mock"/> wires every data service to its in-memory mock;
/// a future <c>Live</c> variant swaps them for API-backed implementations.
/// </summary>
public static class AppDependencies
{
    public static IServiceProvider Mock()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAuthService, MockAuthService>();
        services.AddSingleton<IShowcaseContentProvider, MockShowcaseContentProvider>();
        services.AddSingleton<IServicePlanRepository, MockServicePlanRepository>();
        services.AddSingleton<IBackgroundRepository, MockBackgroundRepository>();
        services.AddSingleton<IBibleRepository, MockBibleRepository>();
        services.AddSingleton<ILibraryRepository, MockLibraryRepository>();
        services.AddSingleton<IMediaPlaybackService, MockMediaPlaybackService>();

        // One shared store behind the four church repositories, so every screen sees the same data.
        services.AddSingleton<InMemoryChurchStore>();
        services.AddSingleton<IModuleSettingsRepository, MockModuleSettingsRepository>();
        services.AddSingleton<IServiceTypeRepository, MockServiceTypeRepository>();
        services.AddSingleton<IPeopleRepository, MockPeopleRepository>();
        services.AddSingleton<ITimeRecordRepository, MockTimeRecordRepository>();

        // The TV output is local hardware, not API data: even the mock build drives the real
        // second-monitor window. Swap in MockDisplayOutputService to fake a connected TV.
        services.AddSingleton<IDisplayOutputService, ProjectionDisplayService>();

        AddApp(services);
        return services.BuildServiceProvider();
    }

    private static void AddApp(IServiceCollection services)
    {
        services.AddSingleton<SessionStore>();
        services.AddSingleton<SignedInNavigator>();
        services.AddSingleton<SessionScope>();
        services.AddSingleton<TopBarViewModel>();
        services.AddTransient<AuthViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<LiveConsoleViewModel>();
        services.AddTransient<ModulesViewModel>();
        services.AddTransient<PeopleViewModel>();
        services.AddTransient<ServiceTypesViewModel>();
        services.AddTransient<TimesViewModel>();
    }
}
