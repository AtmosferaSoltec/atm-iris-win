using System;

namespace Iris.Core.Services;

/// <summary>Runs work on the UI thread. Background services (sync, connectivity, auth) never touch view models directly.</summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    /// <summary>Runs <paramref name="action"/> now when already on the UI thread, otherwise queues it.</summary>
    void Post(Action action);
}

/// <summary>Runs everything inline (tests).</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;

    public void Post(Action action) => action();
}
