using System;

namespace Iris.Core.Networking;

/// <summary>Where data comes from. Mock: in-memory design data. Fake: real code against the in-app fake API. Live: the real API.</summary>
public enum DataMode
{
    Mock,
    Fake,
    Live,
}

/// <summary>Connection settings, persisted by the shell (LocalSettings).</summary>
public interface IAppSettings
{
    DataMode DataMode { get; set; }

    string ApiBaseUrl { get; set; }

    /// <summary>Fake mode only: the handler behaves as if there were no network.</summary>
    bool SimulateOffline { get; set; }

    /// <summary>Raised when <see cref="SimulateOffline"/> changes (on any thread).</summary>
    event EventHandler? SimulateOfflineChanged;
}

public sealed class InMemoryAppSettings : IAppSettings
{
    public const string DefaultApiBaseUrl = "http://localhost:3020/api/v1";

    public DataMode DataMode { get; set; } = DataMode.Fake;

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public bool SimulateOffline
    {
        get;
        set
        {
            field = value;
            SimulateOfflineChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SimulateOfflineChanged;
}
