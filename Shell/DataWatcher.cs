using System;
using System.Linq;
using Iris.Core.Persistence;
using Iris.Core.Services;
using Iris.Core.Sync;

namespace Iris.Shell;

/// <summary>
/// Lets an open screen reload quietly when a sync round brings new data of the kinds it shows. The callback runs on the
/// UI thread; dispose the subscription when the screen goes away.
/// </summary>
public sealed class DataWatcher(ISyncService sync, IUiDispatcher ui)
{
    public IDisposable Watch(Action onChanged, params StoreChangeKind[] kinds)
    {
        var active = true;
        void Handler(object? sender, SyncCompleted completed)
        {
            if (kinds.Length == 0 || completed.WasFirstSync || completed.Changed.Overlaps(kinds))
            {
                ui.Post(() =>
                {
                    if (active)
                    {
                        onChanged();
                    }
                });
            }
        }

        sync.Synced += Handler;
        return new Subscription(() =>
        {
            active = false;
            sync.Synced -= Handler;
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
