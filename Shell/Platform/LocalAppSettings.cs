using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Iris.Core.Networking;
using Windows.Storage;

namespace Iris.Shell.Platform;

/// <summary>
/// <see cref="IAppSettings"/> over <c>ApplicationData.LocalSettings</c> (packaged) or a small JSON file (unpackaged dev copy).
/// Defaults: Live against the deployed API (Fake and Mock are chosen in Conexión, Ctrl+Shift+F12).
/// </summary>
public sealed class LocalAppSettings : IAppSettings
{
    private const string ModeKey = "DataMode";
    private const string UrlKey = "ApiBaseUrl";
    private const string OfflineKey = "SimulateOffline";

    private const DataMode DefaultMode = DataMode.Live;

    private readonly ApplicationDataContainer? _container;
    private readonly string _filePath = AppPaths.File("settings.json");
    private readonly Dictionary<string, string> _file = [];

    public LocalAppSettings()
    {
        if (AppPaths.IsPackaged)
        {
            _container = ApplicationData.Current.LocalSettings;
            return;
        }

        try
        {
            if (File.Exists(_filePath))
            {
                _file = JsonSerializer.Deserialize(File.ReadAllText(_filePath), SettingsJson.Default.DictionaryStringString) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
    }

    public DataMode DataMode
    {
        get => Enum.TryParse<DataMode>(Read(ModeKey), out var mode) ? mode : DefaultMode;
        set => Write(ModeKey, value.ToString());
    }

    public string ApiBaseUrl
    {
        get => Read(UrlKey) is { Length: > 0 } url ? url : InMemoryAppSettings.DefaultApiBaseUrl;
        set => Write(UrlKey, value.Trim());
    }

    public bool SimulateOffline
    {
        get => Read(OfflineKey) == "1";
        set
        {
            Write(OfflineKey, value ? "1" : "0");
            SimulateOfflineChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SimulateOfflineChanged;

    private string? Read(string key) =>
        _container is not null ? _container.Values[key] as string : _file.GetValueOrDefault(key);

    private void Write(string key, string value)
    {
        if (_container is not null)
        {
            _container.Values[key] = value;
            return;
        }

        _file[key] = value;
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_file, SettingsJson.Default.DictionaryStringString));
        }
        catch (IOException)
        {
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SettingsJson : System.Text.Json.Serialization.JsonSerializerContext
{
}
