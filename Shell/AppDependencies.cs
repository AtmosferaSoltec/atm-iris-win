using System;
using System.Net;
using System.Net.Http;
using Iris.Core.Auth;
using Iris.Core.Bible;
using Iris.Core.Logging;
using Iris.Core.Media;
using Iris.Core.Networking;
using Iris.Core.Networking.Fake;
using Iris.Core.Services;
using Iris.Core.Persistence;
using Iris.Core.Services.Live;
using Iris.Core.Services.Mocks;
using Iris.Core.Sync;
using Iris.Features.Auth;
using Iris.Features.Connection;
using Iris.Features.Home;
using Iris.Features.LiveConsole;
using Iris.Features.Modules;
using Iris.Features.People;
using Iris.Features.Services;
using Iris.Features.Times;
using Iris.Shell.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Iris.Shell;

/// <summary>
/// Composition root. <see cref="Mock"/> wires every data service to its in-memory mock (design work);
/// <see cref="Live"/> wires the API-backed services, over the real API (<see cref="DataMode.Live"/>) or the in-app
/// fake API (<see cref="DataMode.Fake"/>).
/// </summary>
public static class AppDependencies
{
    public static IServiceProvider Create(IAppSettings settings) =>
        settings.DataMode == DataMode.Mock ? Mock(settings) : Live(settings);

    public static IServiceProvider Mock(IAppSettings? settings = null)
    {
        var services = new ServiceCollection();
        AddInfrastructure(services, settings ?? new InMemoryAppSettings { DataMode = DataMode.Mock });

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

    public static IServiceProvider Live(IAppSettings settings)
    {
        var services = new ServiceCollection();
        AddInfrastructure(services, settings);

        // Still design data in every mode: the lyrics of the sign-in showcase.
        services.AddSingleton<IAuthService, LiveAuthService>();
        services.AddSingleton<IShowcaseContentProvider, MockShowcaseContentProvider>();
        services.AddSingleton<IServicePlanRepository, MockServicePlanRepository>();
        services.AddSingleton<IBackgroundRepository, LiveBackgroundRepository>();
        services.AddSingleton<IBibleRepository, LiveBibleRepository>();
        services.AddSingleton<ILibraryRepository, LiveLibraryRepository>();
        services.AddSingleton<IMediaPlaybackService, LiveMediaPlaybackService>();
        services.AddSingleton<LiveData>();
        services.AddSingleton<IModuleSettingsRepository, LiveModuleSettingsRepository>();
        services.AddSingleton<IServiceTypeRepository, LiveServiceTypeRepository>();
        services.AddSingleton<IPeopleRepository, LivePeopleRepository>();
        services.AddSingleton<ITimeRecordRepository, LiveTimeRecordRepository>();
        services.AddSingleton<IDisplayOutputService, ProjectionDisplayService>();

        AddApp(services);
        return services.BuildServiceProvider();
    }

    /// <summary>Settings, logging, secure storage and the HTTP stack (the fake API sits under it in Fake mode).</summary>
    private static void AddInfrastructure(IServiceCollection services, IAppSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<ITokenStore, PasswordVaultTokenStore>();
        services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
#if DEBUG
            builder.AddDebug();
#endif
            builder.AddProvider(new FileLoggerProvider(AppPaths.LogsFolder));
        }));
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        if (settings.DataMode == DataMode.Mock)
        {
            services.AddSingleton<ISyncService, NullSyncService>();
            services.AddSingleton<IMediaCache, NullMediaCache>();
            return;
        }

        services.AddSingleton(_ => new FakeDbStore(AppPaths.File("fake-api.json")));
        services.AddSingleton(sp => new FakeIrisApiHandler(sp.GetRequiredService<FakeDbStore>(), () => sp.GetRequiredService<IAppSettings>().SimulateOffline));
        services.AddSingleton<ISessionFile>(_ => new FileSessionFile(AppPaths.File("session.json")));
        services.AddSingleton(sp => new DownloadClient(new HttpClient(sp.GetRequiredService<Transport>().Handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(30) }));
        services.AddSingleton(sp => new Transport(CreateTransport(sp, settings)));

        // Two HTTP stacks over the same transport: the manager's has no AuthHandler (it would call back into the
        // manager while refreshing); everything else goes through AuthHandler for tokens, refresh and retry.
        services.AddSingleton(sp => new AuthSessionManager(
            new ApiClient(NewClient(sp.GetRequiredService<Transport>().Handler, settings), sp.GetRequiredService<ILogger<ApiClient>>()),
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<ISessionFile>(),
            sp.GetRequiredService<ILogger<AuthSessionManager>>()));
        services.AddSingleton(sp => new ApiClient(
            NewClient(new AuthHandler(sp.GetRequiredService<AuthSessionManager>()) { InnerHandler = sp.GetRequiredService<Transport>().Handler }, settings),
            sp.GetRequiredService<ILogger<ApiClient>>()));
        AddSync(services, settings);
    }

    private static void AddSync(IServiceCollection services, IAppSettings settings)
    {
        services.AddSingleton<IConnectivity, NetworkConnectivity>();
        services.AddSingleton(sp => new LocalStore(
            AppPaths.File(settings.DataMode == DataMode.Fake ? "iris-fake.db" : "iris.db"),
            sp.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<Outbox>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<SyncEngine>());
        services.AddSingleton<IPostSyncTask, MediaCacheTask>();
        services.AddSingleton<IPostSyncTask, BibleSyncTask>();
        services.AddSingleton(sp => new BibleStore(
            sp.GetRequiredService<ApiClient>(),
            ModeFolder(AppPaths.LocalFolder, settings),
            sp.GetRequiredService<ILogger<BibleStore>>()));
        services.AddSingleton<PostSyncCoordinator>();
        services.AddSingleton<IMediaCache>(sp => new MediaCache(
            sp.GetRequiredService<LocalStore>(),
            sp.GetRequiredService<ApiClient>(),
            () => sp.GetRequiredService<DownloadClient>().Http,
            sp.GetRequiredService<ISessionContext>(),
            ModeFolder(AppPaths.CacheFolder, settings),
            null,
            sp.GetRequiredService<ILogger<MediaCache>>()));
    }

    /// <summary>Fake-API data (files, Bible) lives apart from real data so switching modes never mixes them.</summary>
    private static string ModeFolder(string root, IAppSettings settings) =>
        settings.DataMode == DataMode.Fake ? System.IO.Path.Combine(root, "fake") : root;

    private sealed record Transport(HttpMessageHandler Handler);

    /// <summary>HTTP client for signed file URLs: no auth handler (the URL carries its own signature); in Fake mode it reaches the fake storage.</summary>
    private sealed record DownloadClient(HttpClient Http);

    private static HttpClient NewClient(HttpMessageHandler handler, IAppSettings settings) =>
        new(handler, disposeHandler: false) { BaseAddress = BaseAddress(settings), Timeout = TimeSpan.FromSeconds(20) };

    private static HttpMessageHandler CreateTransport(IServiceProvider sp, IAppSettings settings) =>
        settings.DataMode == DataMode.Fake
            ? sp.GetRequiredService<FakeIrisApiHandler>()
            : new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, ConnectTimeout = TimeSpan.FromSeconds(8) };

    /// <summary>The API base URL always ends with "/" so relative paths ("auth/sign-in") resolve under it.</summary>
    public static Uri BaseAddress(IAppSettings settings)
    {
        var url = settings.DataMode == DataMode.Fake ? "http://fake.iris.local/api/v1" : settings.ApiBaseUrl.Trim();
        return new Uri(url.TrimEnd('/') + "/");
    }

    private static void AddApp(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var session = sp.GetRequiredService<SessionStore>();
            return new Iris.Core.Models.ChurchClock(() => session.Session?.Church.TimeZone ?? TimeZoneInfo.Local);
        });
        services.AddSingleton<Iris.Core.Services.IUiDispatcher, DispatcherQueueUiDispatcher>();
        services.AddSingleton<IDialogService, ContentDialogService>();
        services.AddSingleton<SessionStore>();
        services.AddSingleton<ISessionContext>(sp => sp.GetRequiredService<SessionStore>());
        services.AddSingleton<SignedInNavigator>();
        services.AddSingleton<SessionScope>();
        services.AddSingleton<TopBarViewModel>();
        services.AddSingleton<SyncIndicatorViewModel>();
        services.AddSingleton<DataWatcher>();
        services.AddTransient<AuthViewModel>();
        services.AddTransient<ConnectionViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<LiveConsoleViewModel>();
        services.AddTransient<ModulesViewModel>();
        services.AddTransient<PeopleViewModel>();
        services.AddTransient<ServiceTypesViewModel>();
        services.AddTransient<TimesViewModel>();
    }
}
