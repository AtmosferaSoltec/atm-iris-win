using System.Net.Http;
using Iris.Core.Auth;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Fake;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iris.Tests;

/// <summary>The real networking stack over the in-app fake API, with a clock the tests can move.</summary>
public sealed class TestStack
{
    public TestStack(bool offline = false, TimeSpan? latency = null)
    {
        Offline = offline;
        Fake = new FakeIrisApiHandler(new FakeDbStore(null), () => Offline, () => Now, latency ?? TimeSpan.Zero);
        Tokens = new InMemoryTokenStore();
        File = new InMemorySessionFile();
        var plain = new ApiClient(NewClient(Fake), NullLogger<ApiClient>.Instance);
        Auth = new AuthSessionManager(plain, Tokens, File, NullLogger<AuthSessionManager>.Instance, () => Now);
        Handler = new AuthHandler(Auth) { InnerHandler = Fake };
        Api = new ApiClient(NewClient(Handler), NullLogger<ApiClient>.Instance);
        Service = new LiveAuthService(Api, Auth);
    }

    public DateTimeOffset Now { get; set; } = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    public bool Offline { get; set; }

    public FakeIrisApiHandler Fake { get; }

    public InMemoryTokenStore Tokens { get; }

    public InMemorySessionFile File { get; }

    public AuthSessionManager Auth { get; }

    public AuthHandler Handler { get; }

    public ApiClient Api { get; }

    public LiveAuthService Service { get; }

    public void Advance(TimeSpan span) => Now += span;

    public Task<UserSession> SignInAsync(string email = "pastor@vidanueva.org", string password = FakeSeed.Password) =>
        Service.SignInAsync(new SignInCredentials(email, password));

    public static HttpClient NewClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("http://fake.iris.local/api/v1/") };
}

/// <summary>A signed-in stack plus the local copy, outbox and sync engine (SQLite in memory).</summary>
public sealed class SyncTestStack : IDisposable
{
    public SyncTestStack(string email = "pastor@vidanueva.org")
    {
        Stack = new TestStack();
        Store = new Iris.Core.Persistence.LocalStore(null);
        Connectivity = new ManualConnectivity();
        Outbox = new Iris.Core.Sync.Outbox(Store, Stack.Api, NullLogger<Iris.Core.Sync.Outbox>.Instance, () => Stack.Now);
        Engine = new Iris.Core.Sync.SyncEngine(Stack.Api, Store, Outbox, Stack.Auth, Connectivity, NullLogger<Iris.Core.Sync.SyncEngine>.Instance, () => Stack.Now);
        Email = email;
    }

    public string Email { get; }

    public TestStack Stack { get; }

    public Iris.Core.Persistence.LocalStore Store { get; }

    public ManualConnectivity Connectivity { get; }

    public Iris.Core.Sync.Outbox Outbox { get; }

    public Iris.Core.Sync.SyncEngine Engine { get; }

    public Guid ChurchId { get; private set; }

    public Iris.Core.Services.FixedSessionContext Session { get; } = new(null);

    public Iris.Core.Services.Live.LiveData Data => _data ??= new(Store, Outbox, Engine, Session);

    private Iris.Core.Services.Live.LiveData? _data;

    public async Task SignInAsync()
    {
        var session = await Stack.SignInAsync(Email);
        ChurchId = session.Church.Id;
        Session.Current = session;
        await Engine.BindChurchAsync(ChurchId);
    }

    public void Dispose()
    {
        Engine.Dispose();
        Store.Dispose();
    }
}
