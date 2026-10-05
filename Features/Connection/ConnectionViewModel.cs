using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Networking;
using Iris.Core.Networking.Fake;
using Iris.Core.Services;
using Iris.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Iris.Features.Connection;

/// <summary>
/// The "Conexión" dialog (development and integration): data mode, API address, connection test, and the
/// fake API's switches. Opened with Ctrl+Shift+F12 on the access screen.
/// </summary>
public sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly IAppSettings _settings;
    private readonly IServiceProvider _services;

    public ConnectionViewModel(IAppSettings settings, IServiceProvider services)
    {
        _settings = settings;
        _services = services;
        ModeIndex = (int)settings.DataMode;
        ApiUrl = settings.ApiBaseUrl;
        SimulateOffline = settings.SimulateOffline;
    }

    public IList<string> Modes { get; } = ["Mock", "Fake", "Live"];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFake), nameof(IsLive), nameof(ModeDescription))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    public partial string ApiUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? UrlError { get; set; }

    [ObservableProperty]
    public partial bool SimulateOffline { get; set; }

    [ObservableProperty]
    public partial string? OkMessage { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsTesting { get; set; }

    public bool IsIdle => !IsTesting;

    public bool IsFake => ModeIndex == (int)DataMode.Fake;

    public bool IsLive => ModeIndex == (int)DataMode.Live;

    public string ModeDescription => (DataMode)ModeIndex switch
    {
        DataMode.Mock => "Datos de diseño en memoria. No usa la red.",
        DataMode.Fake => "Código real contra una API falsa dentro de la app.",
        _ => "Código real contra la API de Iris.",
    };

    /// <summary>The running mode differs from the selected one, or the address changed.</summary>
    public bool NeedsRestart => (DataMode)ModeIndex != _settings.DataMode || (IsLive && ApiUrl.Trim() != _settings.ApiBaseUrl);

    public void Open()
    {
        ModeIndex = (int)_settings.DataMode;
        ApiUrl = _settings.ApiBaseUrl;
        SimulateOffline = _settings.SimulateOffline;
        UrlError = OkMessage = ErrorMessage = null;
        IsOpen = true;
    }

    partial void OnModeIndexChanged(int value) => OkMessage = ErrorMessage = UrlError = null;

    partial void OnApiUrlChanged(string value) => OkMessage = ErrorMessage = UrlError = null;

    // The switch acts at once: the fake handler reads it on every request.
    partial void OnSimulateOfflineChanged(bool value) => _settings.SimulateOffline = value;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task TestAsync()
    {
        if (IsTesting)
        {
            return;
        }

        var mode = (DataMode)ModeIndex;
        if (mode == DataMode.Mock)
        {
            Report("Modo Mock: no usa la red.", ok: true);
            return;
        }

        if (mode == DataMode.Live && !ConnectionProbe.IsValidUrl(ApiUrl))
        {
            UrlError = "Escribe una dirección válida, por ejemplo http://192.168.1.20:3020/api/v1";
            return;
        }

        IsTesting = true;
        try
        {
            string result;
            if (mode == DataMode.Fake)
            {
                var running = _settings.DataMode == DataMode.Fake ? _services.GetService<FakeIrisApiHandler>() : null;
                var handler = running ?? new FakeIrisApiHandler(new FakeDbStore(null), () => SimulateOffline, latency: TimeSpan.Zero);
                result = await ConnectionProbe.TestAsync(handler, new Uri("http://fake.iris.local/api/v1/"), disposeHandler: false);
            }
            else
            {
                var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
                result = await ConnectionProbe.TestAsync(handler, new Uri(ApiUrl.Trim().TrimEnd('/') + "/"), disposeHandler: true);
            }

            Report(result, ok: result == "Conectado");
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void SaveAndRestart()
    {
        var mode = (DataMode)ModeIndex;
        if (mode == DataMode.Live && !ConnectionProbe.IsValidUrl(ApiUrl))
        {
            UrlError = "Escribe una dirección válida, por ejemplo http://192.168.1.20:3020/api/v1";
            return;
        }

        _settings.DataMode = mode;
        if (mode == DataMode.Live)
        {
            _settings.ApiBaseUrl = ApiUrl.Trim();
        }

        try
        {
            // Rebuilds the composition from scratch: the cleanest way to swap every service.
            Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
        }
        catch (Exception)
        {
            Report("Guardado. Cierra y vuelve a abrir Iris para aplicar los cambios.", ok: true);
        }
    }

    /// <summary>Fake mode: writes a song into the fake church as if it had been created on the web; it shows up after the next sync.</summary>
    [RelayCommand]
    private void AddTestSong()
    {
        var handler = _services.GetService<FakeIrisApiHandler>();
        var church = _services.GetService<ISessionContext>()?.Current?.Church.Id;
        if (handler is null || church is null)
        {
            Report("Inicia sesión en modo Fake para agregar una canción de prueba.", ok: false);
            return;
        }

        var song = handler.AddTestSong(church.Value);
        Report($"Agregamos «{song.Title}» a la API falsa. Pulsa «Actualizar ahora» en el indicador de sincronización para verla.", ok: true);
    }

    /// <summary>Fake mode: forget the fake API's data and reseed it on the next start.</summary>
    [RelayCommand]
    private void ResetFakeData()
    {
        _services.GetService<FakeIrisApiHandler>()?.ResetState();
        Report("Datos de la API falsa restablecidos. Cierra la sesión y vuelve a entrar.", ok: true);
    }

    private void Report(string message, bool ok)
    {
        OkMessage = ok ? message : null;
        ErrorMessage = ok ? null : message;
    }
}
