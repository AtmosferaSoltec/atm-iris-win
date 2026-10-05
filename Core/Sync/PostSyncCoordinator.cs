using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Media;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Sync;

/// <summary>Background work that follows a finished sync round (downloading files, the Bible…).</summary>
public interface IPostSyncTask
{
    Task RunAsync(CancellationToken ct);
}

/// <summary>Runs the file cache as a post-sync task.</summary>
public sealed class MediaCacheTask(IMediaCache cache) : IPostSyncTask
{
    public Task RunAsync(CancellationToken ct) => cache.ReconcileAsync(ct);
}

/// <summary>
/// Starts the <see cref="IPostSyncTask"/>s every time a sync round ends without trouble (syncing → idle). One run of
/// each at a time; a request that arrives during a run is dropped because the run already sees the newest copy.
/// </summary>
public sealed class PostSyncCoordinator : IDisposable
{
    private readonly ISyncService _sync;
    private readonly IReadOnlyList<IPostSyncTask> _tasks;
    private readonly ILogger<PostSyncCoordinator> _log;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _wasSyncing;
    private int _running;

    public PostSyncCoordinator(ISyncService sync, IEnumerable<IPostSyncTask> tasks, ILogger<PostSyncCoordinator> log)
    {
        _sync = sync;
        _tasks = tasks.ToList();
        _log = log;
        _sync.StatusChanged += OnStatusChanged;
    }

    public void Dispose()
    {
        _sync.StatusChanged -= OnStatusChanged;
        _lifetime.Cancel();
    }

    private void OnStatusChanged(object? sender, SyncStatus status)
    {
        var finished = _wasSyncing && status.Phase == SyncPhase.Idle;
        _wasSyncing = status.Phase == SyncPhase.Syncing;
        if (finished)
        {
            _ = RunAsync();
        }
    }

    private async Task RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        try
        {
            await Task.WhenAll(_tasks.Select(task => Safe(task)));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task Safe(IPostSyncTask task)
    {
        try
        {
            await task.RunAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.LogWarning("Una tarea posterior a la sincronización falló: {Error}", ex.Message);
        }
    }
}
