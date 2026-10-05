using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Services;

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
    private CancellationTokenSource? _clock;
    private ExternalDisplay? _connected;

    public TopBarViewModel(SessionStore session, SignedInNavigator navigator, IDisplayOutputService display)
    {
        _session = session;
        _navigator = navigator;
        _display = display;
        _session.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    [ObservableProperty]
    public partial string ClockText { get; set; } = Spanish.Time(DateTime.Now);

    public bool IsDisplayConnected => _connected is not null;

    public string DisplayTitle => _connected?.Name ?? "Sin pantalla";

    public string DisplayDetail => _connected?.Resolution ?? "Conecta un TV";

    public string ChurchName => _session.Session?.ChurchName ?? string.Empty;

    /// <summary>Initials of the first two words longer than two letters ("Iglesia Vida Nueva" → "IV").</summary>
    public string AccountInitials => string.Concat(ChurchName
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(w => w.Length > 2)
        .Take(2)
        .Select(w => char.ToUpper(w[0], Spanish.Culture)));

    /// <summary>Probes the TV and starts the minute clock (idempotent).</summary>
    public void Activate()
    {
        _connected = _display.ConnectedDisplay();
        OnPropertyChanged(nameof(IsDisplayConnected));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayDetail));
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
    private void SignOut()
    {
        _clock?.Cancel();
        _clock = null;
        _display.Present(ProjectionFrame.Black);
        _session.SignOut();
        _navigator.Reset();
    }

    private async Task RunClock(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.Now;
                ClockText = Spanish.Time(now);
                await Task.Delay(TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
