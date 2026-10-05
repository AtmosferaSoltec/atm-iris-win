using System;
using System.Threading.Tasks;

namespace Iris.Core.Sync;

/// <summary>
/// What screens and the session need from synchronization. <see cref="SyncEngine"/> is the real one;
/// <see cref="NullSyncService"/> stands in for Mock (design) mode, where there is nothing to sync.
/// </summary>
public interface ISyncService
{
    SyncStatus Status { get; }

    /// <summary>Raised on any thread whenever <see cref="Status"/> changes.</summary>
    event EventHandler<SyncStatus>? StatusChanged;

    /// <summary>A round finished with new data (or the first sync finished). Raised on any thread.</summary>
    event EventHandler<SyncCompleted>? Synced;

    /// <summary>A queued write was refused for good and dropped. Raised on any thread.</summary>
    event EventHandler<DiscardedWrite>? WriteDiscarded;

    /// <summary>A queued write got 403: refresh the session to learn the current permissions. Raised on any thread.</summary>
    event EventHandler? PermissionsMayHaveChanged;

    Task BindChurchAsync(Guid churchId);

    /// <summary>Wipes the local copy and the outbox, and forgets the church.</summary>
    Task ResetAsync();

    Task SyncNowAsync(SyncReason reason);

    /// <summary>Pauses pulls while a service runs; pushes continue.</summary>
    void Suspend();

    void Resume();

    bool IsSuspended { get; }
}

public sealed class NullSyncService : ISyncService
{
    public bool IsSuspended => false;

    public SyncStatus Status => SyncStatus.Initial;

#pragma warning disable CS0067 // Never raised: nothing syncs in Mock mode.
    public event EventHandler<SyncStatus>? StatusChanged;

    public event EventHandler<SyncCompleted>? Synced;

    public event EventHandler<DiscardedWrite>? WriteDiscarded;

    public event EventHandler? PermissionsMayHaveChanged;
#pragma warning restore CS0067

    public Task BindChurchAsync(Guid churchId) => Task.CompletedTask;

    public Task ResetAsync() => Task.CompletedTask;

    public Task SyncNowAsync(SyncReason reason) => Task.CompletedTask;

    public void Suspend()
    {
    }

    public void Resume()
    {
    }
}
