using System;
using System.Linq;
using Iris.Core.Networking;
using Windows.Networking.Connectivity;

namespace Iris.Shell.Platform;

/// <summary>
/// <see cref="IConnectivity"/> from Windows network status. In Fake mode only the "Simular sin conexión" switch counts
/// (the fake API needs no network). A LAN with no internet still counts as online: the API can live on the local network.
/// </summary>
public sealed class NetworkConnectivity : IConnectivity
{
    private readonly IAppSettings _settings;

    public NetworkConnectivity(IAppSettings settings)
    {
        _settings = settings;
        _settings.SimulateOfflineChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            NetworkInformation.NetworkStatusChanged += _ => Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
        }
    }

    public event EventHandler? Changed;

    public bool IsOnline
    {
        get
        {
            if (_settings.SimulateOffline)
            {
                return false;
            }

            if (_settings.DataMode == DataMode.Fake)
            {
                return true;
            }

            try
            {
                return NetworkInformation.GetConnectionProfiles()
                    .Any(p => p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
