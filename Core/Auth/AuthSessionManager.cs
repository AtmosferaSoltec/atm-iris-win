using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Auth;

public enum RefreshResult
{
    /// <summary>New tokens in hand.</summary>
    Refreshed,

    /// <summary>The server rejected the refresh token: the session is over and local data was cleared.</summary>
    SignedOut,

    /// <summary>No connection or a server error: keep the session and try again later.</summary>
    Unavailable,

    /// <summary>There is no session to refresh.</summary>
    NoSession,
}

/// <summary>Keeps the last session view (no tokens) so the app can open offline (<c>session.json</c>).</summary>
public interface ISessionFile
{
    SessionViewDto? Load();

    void Save(SessionViewDto view);

    void Clear();
}

/// <summary>
/// Owner of the tokens (api-contract §4.1): the access token in memory, the refresh token in secure storage.
/// Refreshes before the access token expires and, on a 401, once — with every concurrent caller sharing the
/// same refresh in flight (single flight).
/// </summary>
public sealed class AuthSessionManager
{
    /// <summary>Refresh when less than this is left on the access token.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly ApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly ISessionFile _file;
    private readonly Func<DateTimeOffset> _now;
    private readonly ILogger<AuthSessionManager> _log;
    private readonly object _gate = new();
    private Task<RefreshResult>? _inFlight;
    private string? _accessToken;
    private DateTimeOffset _accessExpires;
    private string? _refreshToken;
    private DateTimeOffset _refreshExpires;
    private string? _userId;

    /// <param name="api">A client whose pipeline has <b>no</b> <see cref="AuthHandler"/> (it would call back here).</param>
    public AuthSessionManager(ApiClient api, ITokenStore tokens, ISessionFile file, ILogger<AuthSessionManager> log, Func<DateTimeOffset>? now = null)
    {
        _api = api;
        _tokens = tokens;
        _file = file;
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The server rejected the refresh token. Raised on a background thread.</summary>
    public event EventHandler? SessionExpired;

    /// <summary>New session data arrived (refresh, switch church). Raised on a background thread.</summary>
    public event EventHandler<SessionViewDto>? SessionUpdated;

    public bool HasSession => _refreshToken is not null;

    /// <summary>The access token currently in memory (even if about to expire).</summary>
    public string? CurrentAccessToken => _accessToken;

    /// <summary>Stores the result of a sign-in, sign-up, refresh or church switch.</summary>
    public void Adopt(AuthResultDto result)
    {
        lock (_gate)
        {
            _accessToken = result.AccessToken;
            _accessExpires = result.AccessTokenExpiresAt;
            _refreshToken = result.RefreshToken;
            _refreshExpires = result.RefreshTokenExpiresAt;
            _userId = result.User.Id.ToString();
        }

        _tokens.Save(new StoredTokens(_userId, result.RefreshToken, result.RefreshTokenExpiresAt));
        _file.Save(result.ToView());
    }

    /// <summary>Updates the stored session view without touching tokens (<c>GET /auth/me</c>).</summary>
    public void UpdateView(SessionViewDto view) => _file.Save(view);

    /// <summary>
    /// Loads what survived the last run. Local only (never touches the network): the stored view, or null
    /// when there is nothing or the refresh token has already expired.
    /// </summary>
    public SessionViewDto? Restore()
    {
        var stored = _tokens.Load();
        var view = _file.Load();
        if (stored is null || view is null)
        {
            ClearLocal();
            return null;
        }

        if (stored.RefreshExpiresAt <= _now())
        {
            ClearLocal();
            return null;
        }

        lock (_gate)
        {
            _refreshToken = stored.RefreshToken;
            _refreshExpires = stored.RefreshExpiresAt;
            _userId = stored.UserId;
            _accessToken = null;
            _accessExpires = DateTimeOffset.MinValue;
        }

        return view;
    }

    /// <summary>Forgets tokens and the stored session (sign out, expiry).</summary>
    public void ClearLocal()
    {
        lock (_gate)
        {
            _accessToken = null;
            _refreshToken = null;
            _accessExpires = _refreshExpires = DateTimeOffset.MinValue;
            _userId = null;
        }

        _tokens.Clear();
        _file.Clear();
    }

    /// <summary>
    /// The token to put on a request, refreshing first when it is missing or about to expire. A network failure
    /// while refreshing returns whatever token there is (the request itself will then fail or be retried).
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_refreshToken is null)
        {
            return null;
        }

        if (_accessToken is null || _accessExpires - _now() < RefreshMargin)
        {
            await RefreshAsync(ct);
        }

        return _accessToken;
    }

    /// <summary>Refreshes the tokens. Concurrent calls await the same request.</summary>
    public Task<RefreshResult> RefreshAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_refreshToken is null)
            {
                return Task.FromResult(RefreshResult.NoSession);
            }

            return _inFlight ??= RefreshCoreAsync(_refreshToken);
        }
    }

    private async Task<RefreshResult> RefreshCoreAsync(string refreshToken)
    {
        // Yield first so the in-flight task is registered before it can possibly finish.
        await Task.Yield();
        try
        {
            var request = ApiRequest.Send(HttpMethod.Post, "auth/refresh", new RefreshBodyDto(refreshToken), IrisJsonContext.Default.RefreshBodyDto, anonymous: true);
            var result = await _api.SendAsync(request, IrisJsonContext.Default.AuthResultDto);
            Adopt(result);
            _log.LogInformation("Sesión renovada; el access token vence {Expires:u}", result.AccessTokenExpiresAt);
            SessionUpdated?.Invoke(this, result.ToView());
            return RefreshResult.Refreshed;
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            _log.LogWarning("El refresh fue rechazado ({Code}): se cierra la sesión", ex.Code);
            ClearLocal();
            SessionExpired?.Invoke(this, EventArgs.Empty);
            return RefreshResult.SignedOut;
        }
        catch (Exception ex) when (ex is NetworkException or ApiException)
        {
            // No connection or a 5xx: the session stays; the console keeps working with its local copy.
            _log.LogWarning("No se pudo renovar la sesión ahora ({Reason})", ex.GetType().Name);
            return RefreshResult.Unavailable;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
            }
        }
    }
}
