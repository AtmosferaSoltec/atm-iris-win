using System;

namespace Iris.Core.Networking;

/// <summary>
/// Whether the network is believed to be reachable. Only a hint to avoid pointless attempts: a request can still fail
/// when this says online, and the sync engine treats that as offline too.
/// </summary>
public interface IConnectivity
{
    bool IsOnline { get; }

    /// <summary>Raised on any thread when <see cref="IsOnline"/> may have changed.</summary>
    event EventHandler? Changed;
}

/// <summary>Settable connectivity for tests.</summary>
public sealed class ManualConnectivity(bool online = true) : IConnectivity
{
    private bool _online = online;

    public bool IsOnline
    {
        get => _online;
        set
        {
            if (_online != value)
            {
                _online = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? Changed;
}
