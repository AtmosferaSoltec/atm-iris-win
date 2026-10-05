using System;
using Iris.Core.Services;
using Microsoft.UI.Dispatching;

namespace Iris.Shell.Platform;

/// <summary><see cref="IUiDispatcher"/> over the UI thread's <see cref="DispatcherQueue"/> (created on that thread).</summary>
public sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();

    public bool IsOnUiThread => _queue.HasThreadAccess;

    public void Post(Action action)
    {
        if (_queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _queue.TryEnqueue(() => action());
        }
    }
}
