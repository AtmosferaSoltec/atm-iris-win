using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Updates;
using Iris.Shell;

namespace Iris.Features.Updates;

/// <summary>
/// The "Hay una nueva versión" sheet that cannot be dismissed (docs/plans/12). It only opens when the API says this
/// version is too old <b>and</b> the feed has the new one, and never over a running service.
/// </summary>
public sealed partial class UpdateGateViewModel : ObservableObject
{
    private readonly IAppVersionApi _api;
    private readonly IInstaller _installer;
    private readonly SignedInNavigator _navigator;
    private bool _checking;

    public UpdateGateViewModel(IAppVersionApi api, IInstaller installer, SignedInNavigator navigator)
    {
        _api = api;
        _installer = installer;
        _navigator = navigator;
        _navigator.PropertyChanged += OnNavigatorChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Message))]
    public partial RequiredUpdate? Required { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ProgressText))]
    public partial bool IsDownloading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial int Percent { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    public bool IsIdle => !IsDownloading;

    /// <summary>Closed while a service is running: the console keeps working until the person is back at Inicio.</summary>
    public bool IsOpen => Required is not null && _navigator.Route != AppRoute.Console;

    public string Message => Required is null ? string.Empty : $"Para continuar, actualiza a la versión {Required.Version}.";

    public string ProgressText => IsDownloading ? $"Descargando… {Percent} %" : string.Empty;

    /// <summary>Asks once per launch (and again when back at Inicio until it is known). Safe to call often.</summary>
    public async Task CheckAsync()
    {
        if (Required is not null || _checking || _navigator.Route == AppRoute.Console)
        {
            return;
        }

        _checking = true;
        try
        {
            Required = await UpdateCheck.RunAsync(_api, _installer, AppVersion.Current());
        }
        finally
        {
            _checking = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        Error = null;
        IsDownloading = true;
        Percent = 0;
        try
        {
            await _installer.DownloadAsync(new Progress<int>(value => Percent = value), CancellationToken.None);
            _installer.ApplyAndRestart();
        }
        catch (Exception)
        {
            Error = "No pudimos descargar la actualización. Revisa tu conexión e inténtalo de nuevo.";
            IsDownloading = false;
        }
    }

    private void OnNavigatorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignedInNavigator.Route))
        {
            OnPropertyChanged(nameof(IsOpen));
        }
    }
}
