using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Core.Sync;

namespace Iris.Shell;

/// <summary>A one-time message the access screen shows (expired session, password updated).</summary>
public sealed record AccessNotice(string Message, bool IsSuccess);

/// <summary>
/// Holds the signed-in session. No session → access screen; session → live console. Also owns the session
/// lifecycle: restore at launch (offline-safe), expiry reported by the auth layer, and sign-out.
/// </summary>
public sealed partial class SessionStore : ObservableObject, Iris.Core.Services.ISessionContext
{
    private readonly IAuthService _auth;
    private readonly SignedInNavigator _navigator;
    private readonly IDisplayOutputService _display;
    private readonly IUiDispatcher _ui;
    private readonly ISyncService _sync;

    public SessionStore(IAuthService auth, SignedInNavigator navigator, IDisplayOutputService display, IUiDispatcher ui, ISyncService sync)
    {
        _sync = sync;
        _sync.PermissionsMayHaveChanged += (_, _) => _ui.Post(() => _ = RefreshInBackgroundAsync());
        _auth = auth;
        _navigator = navigator;
        _display = display;
        _ui = ui;
        _auth.SessionExpired += (_, _) => _ui.Post(OnExpired);
        _auth.SessionUpdated += (_, session) => _ui.Post(() =>
        {
            if (IsSignedIn && session.UserId == Session?.UserId)
            {
                Session = session;
            }
        });
    }

    [ObservableProperty]
    public partial UserSession? Session { get; private set; }

    private bool _wasSignedIn;

    /// <summary>Raises <see cref="INotifyPropertyChanged.PropertyChanged"/> only when the user signs in or out (screens swap), not on session updates.</summary>
    public bool IsSignedIn => Session is not null;

    partial void OnSessionChanged(UserSession? value)
    {
        if ((value is not null) != _wasSignedIn)
        {
            _wasSignedIn = value is not null;
            OnPropertyChanged(nameof(IsSignedIn));
        }
    }

    /// <summary>Kept after signing out so the access screen can prefill it (the password is not kept).</summary>
    public string LastEmail { get; private set; } = string.Empty;

    private AccessNotice? _notice;

    /// <summary>Returns the pending access-screen message once.</summary>
    public AccessNotice? TakeNotice()
    {
        var notice = _notice;
        _notice = null;
        return notice;
    }

    public void PostNotice(AccessNotice notice) => _notice = notice;

    UserSession? Iris.Core.Services.ISessionContext.Current => Session;

    /// <summary>Whether the signed-in role has the permission (false when signed out).</summary>
    public bool Can(Permission permission) => Session?.Can(permission) ?? false;

    public void SignIn(UserSession session)
    {
        LastEmail = session.Email;
        _notice = null;
        Session = session;
        _ = AttachSyncAsync(session);
    }

    /// <summary>
    /// Launch: enters at once with the stored session (no waiting for the network), then refreshes it in the
    /// background. Without a stored session or with an expired one, the access screen shows.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            if (await _auth.RestoreAsync() is { } stored)
            {
                LastEmail = stored.Email;
                Session = stored;
                _ = AttachSyncAsync(stored);
                _ = RefreshInBackgroundAsync();
            }
        }
        catch (Exception)
        {
            Session = null;
        }
    }

    /// <summary>Ends the session. Always succeeds locally, with or without a connection.</summary>
    public async Task SignOutAsync()
    {
        try
        {
            await _auth.SignOutAsync();
        }
        catch (Exception)
        {
        }

        // An explicit sign-out removes the church data from this PC; the next sign-in downloads it again.
        await _sync.ResetAsync();
        Finish();
    }

    /// <summary>Ends the session on every device (web, iPad and Windows), then clears this PC.</summary>
    public async Task SignOutAllAsync()
    {
        await _auth.SignOutAllAsync();
        await _sync.ResetAsync();
        Finish();
    }

    /// <summary>
    /// Moves this session to another church: new tokens, the local copy and the outbox of the old church are wiped, the new
    /// church downloads and Home opens fresh. Throws <see cref="AuthException"/> when the switch cannot happen (offline).
    /// </summary>
    public async Task SwitchChurchAsync(Guid churchId)
    {
        var session = await _auth.SwitchChurchAsync(churchId);
        _display.Present(Core.Models.ProjectionFrame.Black);
        await _sync.ResetAsync();
        Session = session;
        _navigator.GoHome();
        _ = AttachSyncAsync(session);
    }

    /// <summary>Replaces the session data (permissions, churches) without changing screens.</summary>
    public void Update(UserSession session)
    {
        if (IsSignedIn)
        {
            Session = session;
        }
    }

    /// <summary>Tells the sync engine which church this session is in and starts the first (or next) round.</summary>
    private async Task AttachSyncAsync(UserSession session)
    {
        try
        {
            await _sync.BindChurchAsync(session.Church.Id);
            await _sync.SyncNowAsync(SyncReason.SignedIn);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Refreshes the session from the API (permissions, churches) without changing screens. Offline: no-op.</summary>
    public Task RefreshSessionAsync() => RefreshInBackgroundAsync();

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            if (await _auth.CurrentSessionAsync() is { } fresh)
            {
                Update(fresh);
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnExpired()
    {
        if (!IsSignedIn)
        {
            return;
        }

        _notice = new AccessNotice("Tu sesión expiró. Vuelve a iniciar sesión.", IsSuccess: false);
        Finish();
    }

    private void Finish()
    {
        if (Session is { } session)
        {
            LastEmail = session.Email;
        }

        _display.Present(Core.Models.ProjectionFrame.Black);
        Session = null;
        _navigator.Reset();
    }
}
