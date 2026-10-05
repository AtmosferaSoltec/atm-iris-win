using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Features.Auth;
using Iris.Core.Sync;

namespace Iris.Shell;

/// <summary>
/// The trailing half of every signed-in top bar (IRIS_SPEC §6.2): the clock (updates every
/// minute), the TV status chip and the account menu.
/// </summary>
public sealed partial class TopBarViewModel : ObservableObject
{
    private readonly SessionStore _session;
    private readonly SignedInNavigator _navigator;
    private readonly IDisplayOutputService _display;
    private readonly ChurchClock _churchClock;
    private readonly ISyncService _sync;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _clock;
    private ExternalDisplay? _connected;

    public TopBarViewModel(SessionStore session, SignedInNavigator navigator, IDisplayOutputService display, ChurchClock clock, ISyncService sync, IDialogService dialogs)
    {
        _sync = sync;
        _dialogs = dialogs;
        _churchClock = clock;
        ClockText = Spanish.Time(clock.Now);
        _session = session;
        _navigator = navigator;
        _display = display;
        _display.DisplayChanged += (_, _) => RefreshDisplay();
        _session.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    [ObservableProperty]
    public partial string ClockText { get; set; } = string.Empty;

    public bool IsDisplayConnected => _connected is not null;

    public string DisplayTitle => _connected?.Name ?? "Sin pantalla";

    public string DisplayDetail => _connected?.Resolution ?? "Conecta un TV";

    public string FullName => _session.Session?.FullName ?? string.Empty;

    public string Email => _session.Session?.Email ?? string.Empty;

    /// <summary>"Dueño", "Administrador" or "Operador".</summary>
    public string RoleText => _session.Session?.Role switch
    {
        Role.Owner => "Dueño",
        Role.Admin => "Administrador",
        _ => "Operador",
    };

    public string AccountSummary => $"{ChurchName} · {RoleText}";

    public bool HasMultipleChurches => _session.Session?.Churches.Count > 1;

    /// <summary>Every church of the account (the submenu "Cambiar de iglesia").</summary>
    public IReadOnlyList<ChurchSummary> Churches => _session.Session?.Churches ?? [];

    public Guid? CurrentChurchId => _session.Session?.Church.Id;

    public string ChurchName => _session.Session?.Church.Name ?? string.Empty;

    /// <summary>Initials of the first two words longer than two letters ("Iglesia Vida Nueva" → "IV").</summary>
    public string AccountInitials => string.Concat(ChurchName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(w => w.Length > 2)
        .Take(2)
        .Select(w => char.ToUpper(w[0], Spanish.Culture)));

    /// <summary>Every secondary monitor (for "Elegir pantalla").</summary>
    public IReadOnlyList<ExternalDisplay> Displays => _display.Displays();

    public bool HasMultipleDisplays => Displays.Count > 1;

    public string DisplayTooltip => HasMultipleDisplays ? "Elegir pantalla" : "Pantalla del TV";

    public string? CurrentDisplayId => _connected?.Id;

    [RelayCommand]
    private void ChooseDisplay(ExternalDisplay display) => _display.Choose(display);

    // The TV chip follows monitors live: plugging or unplugging updates it with no restart.
    private void RefreshDisplay()
    {
        _connected = _display.ConnectedDisplay();
        OnPropertyChanged(nameof(IsDisplayConnected));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayDetail));
        OnPropertyChanged(nameof(HasMultipleDisplays));
        OnPropertyChanged(nameof(DisplayTooltip));
        OnPropertyChanged(nameof(CurrentDisplayId));
    }

    /// <summary>Probes the TV and starts the minute clock (idempotent).</summary>
    public void Activate()
    {
        RefreshDisplay();
        if (_clock is null)
        {
            _clock = new CancellationTokenSource();
            _ = RunClock(_clock.Token);
        }
    }

    public bool IsTvConnected()
    {
        _connected = _display.ConnectedDisplay();
        return _connected is not null;
    }

    [RelayCommand]
    private async Task SwitchChurchAsync(ChurchSummary church)
    {
        if (church.Id == CurrentChurchId || !await ConfirmDiscardPendingAsync("¿Cambiar de iglesia?", "Cambiar de iglesia"))
        {
            return;
        }

        try
        {
            await _session.SwitchChurchAsync(church.Id);
        }
        catch (AuthException ex)
        {
            await _dialogs.ShowMessageAsync("No se pudo cambiar de iglesia", ex.HasServerMessage ? ex.Message : AuthValidator.Message(ex.Error));
        }
    }

    [RelayCommand]
    private async Task SignOutAllAsync()
    {
        if (!await _dialogs.ConfirmAsync(
                "¿Cerrar sesión en todos los dispositivos?",
                "Se cerrará la sesión en la web, el iPad y Windows. Tendrás que volver a iniciar sesión en cada uno.",
                "Cerrar sesión en todos",
                destructive: true)
            || !await ConfirmDiscardPendingAsync("¿Cerrar sesión?", "Cerrar sesión"))
        {
            return;
        }

        try
        {
            await _session.SignOutAllAsync();
            _clock?.Cancel();
            _clock = null;
        }
        catch (AuthException ex)
        {
            await _dialogs.ShowMessageAsync("No se pudo cerrar la sesión en todos los dispositivos", ex.HasServerMessage ? ex.Message : AuthValidator.Message(ex.Error));
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (!await ConfirmDiscardPendingAsync("¿Cerrar sesión?", "Cerrar sesión"))
        {
            return;
        }

        _clock?.Cancel();
        _clock = null;
        await _session.SignOutAsync();
    }

    /// <summary>With unsent changes, signing out or switching church loses them: ask first.</summary>
    private async Task<bool> ConfirmDiscardPendingAsync(string title, string confirmText) =>
        _sync.Status.Pending == 0
        || await _dialogs.ConfirmAsync(title, PendingWarning(_sync.Status.Pending), confirmText, destructive: true);

    public static string PendingWarning(int count) => count == 1
        ? "Hay 1 cambio sin enviar. Si sigues, se perderá."
        : $"Hay {count} cambios sin enviar. Si sigues, se perderán.";

    private async Task RunClock(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = _churchClock.Now;
                ClockText = Spanish.Time(now);
                await Task.Delay(TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
