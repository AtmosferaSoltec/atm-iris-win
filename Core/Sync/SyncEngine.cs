using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Auth;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Sync;

public enum SyncPhase
{
    Idle,
    Syncing,
    Offline,
    Failed,
}

public enum SyncReason
{
    SignedIn,
    Home,
    Timer,
    Reconnected,
    Manual,
    Write,
    Resumed,
}

/// <summary>What the sync indicator shows (api-contract §12, plan 02 §6).</summary>
public sealed record SyncStatus(
    SyncPhase Phase,
    DateTimeOffset? LastSync,
    int Pending,
    string? Message,
    bool IsFirstSync,
    int ItemsApplied)
{
    public static readonly SyncStatus Initial = new(SyncPhase.Idle, null, 0, null, false, 0);
}

/// <summary>A sync round finished: which tables got new data.</summary>
public sealed record SyncCompleted(IReadOnlySet<StoreChangeKind> Changed, bool WasFirstSync);

/// <summary>
/// Keeps the local copy up to date. Each round first empties the outbox, then pulls <c>GET /sync/changes</c> page by page
/// from the saved cursor until <c>hasMore</c> is false, saving the cursor after every page. One round at a time: a request
/// that arrives meanwhile is chained as one more round. Pulls are paused while a service is running
/// (<see cref="Suspend"/>), because the console works with the copy it had when the service began; pushes are not.
/// </summary>
public sealed class SyncEngine : ISyncService, IDisposable
{
    private const int PageSize = 200;

    private readonly ApiClient _api;
    private readonly LocalStore _store;
    private readonly Outbox _outbox;
    private readonly AuthSessionManager _auth;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<SyncEngine> _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly HashSet<StoreEntity> _toHeal = [];
    private bool _resyncAll;
    private Task? _running;
    private bool _rerun;
    private bool _suspended;
    private bool _pullSkipped;
    private Guid? _churchId;
    private Timer? _retry;
    private SyncStatus _status = SyncStatus.Initial;

    public SyncEngine(
        ApiClient api,
        LocalStore store,
        Outbox outbox,
        AuthSessionManager auth,
        IConnectivity connectivity,
        ILogger<SyncEngine> log,
        Func<DateTimeOffset>? now = null)
    {
        _api = api;
        _store = store;
        _outbox = outbox;
        _auth = auth;
        _connectivity = connectivity;
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _connectivity.Changed += OnConnectivityChanged;
        _store.Changed += OnStoreChanged;
        _outbox.Discarded += OnDiscarded;
    }

    public SyncStatus Status => _status;

    /// <summary>Raised on any thread whenever <see cref="Status"/> changes.</summary>
    public event EventHandler<SyncStatus>? StatusChanged;

    /// <summary>A round finished and applied at least one page (or finished the first sync). Raised on any thread.</summary>
    public event EventHandler<SyncCompleted>? Synced;

    /// <summary>A queued write was refused and dropped. Raised on any thread.</summary>
    public event EventHandler<DiscardedWrite>? WriteDiscarded;

    /// <summary>A queued write got 403: the person's role may have changed. Raised on any thread.</summary>
    public event EventHandler? PermissionsMayHaveChanged;

    public bool IsSuspended => _suspended;

    public void Dispose()
    {
        _connectivity.Changed -= OnConnectivityChanged;
        _store.Changed -= OnStoreChanged;
        _outbox.Discarded -= OnDiscarded;
        _retry?.Dispose();
    }

    /// <summary>
    /// Declares which church the signed-in session belongs to. A copy that belongs to another church is wiped; the same
    /// church keeps its copy and its pending writes (they were made by this PC and are still valid).
    /// </summary>
    public async Task BindChurchAsync(Guid churchId)
    {
        var state = await _store.GetSyncStateAsync();
        if (state.ChurchId is { } stored && stored != churchId)
        {
            _log.LogInformation("La copia local era de otra iglesia: se borra");
            await _store.ClearAsync();
        }

        _churchId = churchId;
        var pending = await _store.CountOutboxAsync();
        var fresh = await _store.GetSyncStateAsync();
        Publish(new SyncStatus(SyncPhase.Idle, fresh.LastSync, pending, null, !fresh.InitialDone, 0));
    }

    /// <summary>Forgets the church binding and wipes the local copy and the outbox (sign out, church switch).</summary>
    public async Task ResetAsync()
    {
        _churchId = null;
        _retry?.Dispose();
        _retry = null;
        await _store.ClearAsync();
        Publish(SyncStatus.Initial);
    }

    public int PendingWrites => _status.Pending;

    /// <summary>Pauses pulls (a service is running). Pushes continue.</summary>
    public void Suspend() => _suspended = true;

    public void Resume()
    {
        _suspended = false;
        if (_pullSkipped)
        {
            _pullSkipped = false;
            _ = SyncNowAsync(SyncReason.Resumed);
        }
    }

    /// <summary>
    /// Runs a sync round (push, then pull). Returns when it and any chained round are done. Never throws: problems end up
    /// in <see cref="Status"/>.
    /// </summary>
    public Task SyncNowAsync(SyncReason reason)
    {
        lock (_gate)
        {
            if (_running is not null)
            {
                _rerun = true;
                return _running;
            }

            return _running = RunLoopAsync(reason);
        }
    }

    private async Task RunLoopAsync(SyncReason first)
    {
        await Task.Yield();
        try
        {
            var reason = first;
            while (true)
            {
                await RunOnceAsync(reason);
                lock (_gate)
                {
                    if (!_rerun)
                    {
                        break;
                    }

                    _rerun = false;
                }

                reason = SyncReason.Write;
            }
        }
        catch (Exception ex)
        {
            _log.LogError("Falló la sincronización: {Error}", ex.Message);
            Publish(_status with { Phase = SyncPhase.Failed, Message = "No se pudo actualizar. Inténtalo de nuevo." });
        }
        finally
        {
            lock (_gate)
            {
                _running = null;
            }
        }
    }

    private async Task RunOnceAsync(SyncReason reason)
    {
        if (_churchId is not { } churchId || !_auth.HasSession)
        {
            return;
        }

        var first = !(await _store.GetSyncStateAsync()).InitialDone;
        var pending = await _store.CountOutboxAsync();
        Publish(_status with { Phase = SyncPhase.Syncing, Pending = pending, Message = null, IsFirstSync = first, ItemsApplied = 0 });
        _log.LogInformation("Sincronizando ({Reason})", reason);

        if (!_connectivity.IsOnline)
        {
            Publish(_status with { Phase = SyncPhase.Offline, Pending = pending });
            return;
        }

        if (await _outbox.FlushAsync() == FlushOutcome.Blocked)
        {
            ScheduleRetry(_outbox.RetryDelay);
            Publish(_status with { Phase = SyncPhase.Offline, Pending = await _store.CountOutboxAsync() });
            return;
        }

        if (_suspended)
        {
            // A service is running: keep the copy it started with.
            _pullSkipped = true;
            Publish(_status with { Phase = SyncPhase.Idle, Pending = 0 });
            return;
        }

        try
        {
            var changed = await PullAsync(churchId);
            var state = await _store.GetSyncStateAsync();
            Publish(new SyncStatus(SyncPhase.Idle, state.LastSync, await _store.CountOutboxAsync(), null, false, _status.ItemsApplied));
            if (changed.Count > 0 || first)
            {
                Synced?.Invoke(this, new SyncCompleted(changed, first));
            }
        }
        catch (NetworkException)
        {
            Publish(_status with { Phase = SyncPhase.Offline, Pending = await _store.CountOutboxAsync() });
        }
        catch (ApiException ex)
        {
            _log.LogWarning("La sincronización recibió {Status} {Code}", ex.StatusCode, ex.Code);
            Publish(_status with { Phase = SyncPhase.Failed, Message = ex.Message, Pending = await _store.CountOutboxAsync() });
        }
    }

    private async Task<HashSet<StoreChangeKind>> PullAsync(Guid churchId)
    {
        var touched = new HashSet<StoreChangeKind>();

        // A queued write the server refused left its optimistic copy behind: drop that table and refetch everything.
        List<StoreEntity> heal;
        bool resyncAll;
        lock (_toHeal)
        {
            heal = [.. _toHeal];
            resyncAll = _resyncAll || heal.Count > 0;
            _toHeal.Clear();
            _resyncAll = false;
        }

        foreach (var entity in heal)
        {
            await _store.ClearEntityAsync(entity);
        }

        if (resyncAll)
        {
            await _store.ResetCursorAsync();
        }

        {
            var cursor = (await _store.GetSyncStateAsync()).Cursor;
            SyncPageDto page;
            do
            {
                page = await _api.SendAsync(ApiRequest.Get($"sync/changes?since={Uri.EscapeDataString(cursor)}&limit={PageSize}"), IrisJsonContext.Default.SyncPageDto);
                await _store.ApplyAsync(page, churchId);
                Collect(page, touched);
                cursor = page.Cursor;
                var count = page.Changes.People.Count + page.Changes.ServiceTypes.Count + page.Changes.Songs.Count + page.Changes.Media.Count
                    + page.Changes.ServiceRecords.Count + page.Deleted.People.Count + page.Deleted.ServiceTypes.Count
                    + page.Deleted.Songs.Count + page.Deleted.Media.Count + page.Deleted.ServiceRecords.Count;
                Publish(_status with { ItemsApplied = _status.ItemsApplied + count });
            }
            while (page.HasMore);

            await _store.MarkSyncedAsync(churchId, _now());
        }

        return touched;
    }

    private static void Collect(SyncPageDto page, HashSet<StoreChangeKind> touched)
    {
        if (page.Church is not null)
        {
            touched.Add(StoreChangeKind.Church);
        }

        if (page.Changes.People.Count + page.Deleted.People.Count > 0)
        {
            touched.Add(StoreChangeKind.People);
        }

        if (page.Changes.ServiceTypes.Count + page.Deleted.ServiceTypes.Count > 0)
        {
            touched.Add(StoreChangeKind.ServiceTypes);
        }

        if (page.Changes.Songs.Count + page.Deleted.Songs.Count > 0)
        {
            touched.Add(StoreChangeKind.Songs);
        }

        if (page.Changes.Media.Count + page.Deleted.Media.Count > 0)
        {
            touched.Add(StoreChangeKind.Media);
        }

        if (page.Changes.ServiceRecords.Count + page.Deleted.ServiceRecords.Count > 0)
        {
            touched.Add(StoreChangeKind.ServiceRecords);
        }
    }

    private void OnConnectivityChanged(object? sender, EventArgs e)
    {
        if (_connectivity.IsOnline)
        {
            _ = SyncNowAsync(SyncReason.Reconnected);
        }
        else if (_status.Phase != SyncPhase.Syncing)
        {
            Publish(_status with { Phase = SyncPhase.Offline });
        }
    }

    private void OnStoreChanged(object? sender, StoreChangeKind kind)
    {
        if (kind is StoreChangeKind.Outbox)
        {
            _ = RefreshPendingAsync();
        }
    }

    private async Task RefreshPendingAsync()
    {
        var pending = await _store.CountOutboxAsync();
        if (pending != _status.Pending)
        {
            Publish(_status with { Pending = pending });
        }
    }

    private void OnDiscarded(object? sender, DiscardedWrite discarded)
    {
        lock (_toHeal)
        {
            if (discarded.Entity is { } entity)
            {
                _toHeal.Add(entity);
            }
            else
            {
                _resyncAll = true;
            }
        }

        WriteDiscarded?.Invoke(this, discarded);
        if (discarded.StatusCode == 403)
        {
            PermissionsMayHaveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ScheduleRetry(TimeSpan delay)
    {
        _retry?.Dispose();
        _retry = new Timer(_ => _ = SyncNowAsync(SyncReason.Write), null, delay, Timeout.InfiniteTimeSpan);
    }

    private void Publish(SyncStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }
}
