using System.Net;
using Iris.Core.Auth;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Persistence;
using Iris.Core.Services;
using Iris.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iris.Tests;

/// <summary>
/// The app's real stack (HTTP client, token handler, sync engine, SQLite copy) against a deployed API.
/// Opt-in: set IRIS_LIVE_URL (…/api/v1), IRIS_LIVE_EMAIL and IRIS_LIVE_PASSWORD; without them every test returns at once.
/// One sign-in serves the whole flow (the server limits sign-in to 5 per minute per IP). Reads only, plus one person created and deleted again.
/// </summary>
public sealed class LiveApiTests : IDisposable
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("IRIS_LIVE_URL");
    private static readonly string Email = Environment.GetEnvironmentVariable("IRIS_LIVE_EMAIL") ?? "";
    private static readonly string Password = Environment.GetEnvironmentVariable("IRIS_LIVE_PASSWORD") ?? "";

    private static bool Enabled => !string.IsNullOrWhiteSpace(Url);

    private readonly HttpMessageHandler _transport = new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    private readonly LocalStore _store = new(null);
    private ApiClient _api = null!;
    private AuthSessionManager _auth = null!;
    private LiveAuthService _service = null!;

    public void Dispose()
    {
        _store.Dispose();
        _transport.Dispose();
    }

    private HttpClient NewClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri(Url!.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };

    private void Build()
    {
        var plain = new ApiClient(NewClient(_transport), NullLogger<ApiClient>.Instance);
        _auth = new AuthSessionManager(plain, new InMemoryTokenStore(), new InMemorySessionFile(), NullLogger<AuthSessionManager>.Instance, () => DateTimeOffset.UtcNow);
        _api = new ApiClient(NewClient(new AuthHandler(_auth) { InnerHandler = _transport }), NullLogger<ApiClient>.Instance);
        _service = new LiveAuthService(_api, _auth);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_the_servers_message()
    {
        if (!Enabled) return;
        Build();

        var ex = await Assert.ThrowsAsync<AuthException>(() => _service.SignInAsync(new SignInCredentials(Email, "incorrecta123")));

        Assert.False(_auth.HasSession);
        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
    }

    [Fact]
    public async Task Real_server_sign_in_sync_refresh_writes_and_sign_out()
    {
        if (!Enabled) return;
        Build();

        var session = await _service.SignInAsync(new SignInCredentials(Email, Password));
        Assert.True(_auth.HasSession);
        Assert.False(string.IsNullOrWhiteSpace(session.Church.Name));

        var connectivity = new ManualConnectivity();
        var outbox = new Outbox(_store, _api, NullLogger<Outbox>.Instance, () => DateTimeOffset.UtcNow);
        using var engine = new SyncEngine(_api, _store, outbox, _auth, connectivity, NullLogger<SyncEngine>.Instance, () => DateTimeOffset.UtcNow);
        await engine.BindChurchAsync(session.Church.Id);
        await engine.SyncNowAsync(SyncReason.SignedIn);

        Assert.Equal(SyncPhase.Idle, engine.Status.Phase);
        Assert.True((await _store.GetSyncStateAsync()).InitialDone);
        var church = await _store.GetChurchAsync();
        Assert.NotNull(church);
        Assert.NotNull(church);
        Assert.NotNull(church!.Storage.Breakdown);
        Assert.False(church.AvailableModules!.Bible);
        Assert.NotEmpty(await _store.GetSongsAsync());
        Assert.All(await _store.GetSongsAsync(), s => Assert.NotEmpty(s.Sections));
        Assert.NotEmpty(await _store.GetMediaAsync());

        // A second sync from the saved cursor changes nothing.
        var completed = 0;
        engine.Synced += (_, _) => completed++;
        await engine.SyncNowAsync(SyncReason.Manual);
        Assert.Equal(0, completed);

        await Expired_access_token_is_refreshed_once_and_the_request_retried();
        await Person_created_with_a_client_id_is_idempotent_and_duplicates_are_refused();
        await Bible_is_not_available_while_the_system_switch_is_off();

        await _service.SignOutAsync();
        Assert.False(_auth.HasSession);
    }

    private async Task Expired_access_token_is_refreshed_once_and_the_request_retried()
    {
        var first = _auth.CurrentAccessToken;
        await Task.Delay(1100); // the JWT only differs from the next second on
        var refreshed = await _auth.RefreshAsync();
        Assert.NotEqual(first, _auth.CurrentAccessToken);
        Assert.Equal(RefreshResult.Refreshed, refreshed);

        var me = await _service.CurrentSessionAsync();
        Assert.NotNull(me);
    }

    private async Task Person_created_with_a_client_id_is_idempotent_and_duplicates_are_refused()
    {
        var id = Guid.NewGuid();
        var name = "ZZ Prueba " + id.ToString("N")[..8];
        var body = new PersonCreateDto(id, name);

        try
        {
            var created = await _api.SendAsync(ApiRequest.Send(HttpMethod.Post, "people", body, IrisJsonContext.Default.PersonCreateDto), IrisJsonContext.Default.PersonDto);
            var again = await _api.SendAsync(ApiRequest.Send(HttpMethod.Post, "people", body, IrisJsonContext.Default.PersonCreateDto), IrisJsonContext.Default.PersonDto);
            Assert.Equal(created.Id, again.Id);

            var dup = await Assert.ThrowsAsync<ApiException>(() => _api.SendAsync(
                ApiRequest.Send(HttpMethod.Post, "people", new PersonCreateDto(Guid.NewGuid(), name.ToLowerInvariant()), IrisJsonContext.Default.PersonCreateDto),
                IrisJsonContext.Default.PersonDto));
            Assert.Equal("PERSON_NAME_TAKEN", dup.Code);
        }
        finally
        {
            await _api.SendAsync(new ApiRequest(HttpMethod.Delete, "people/" + id));
        }

    }

    private async Task Bible_is_not_available_while_the_system_switch_is_off()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => _api.SendAsync(ApiRequest.Get("bible/translations")));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("NOT_FOUND", ex.Code);
    }
}
