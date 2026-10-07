using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Timing;

namespace Iris.Features.LiveConsole;

/// <summary>One quick-start button of the timer flyout ("5 min").</summary>
public sealed class CountdownPresetViewModel(int minutes, CountdownViewModel owner)
{
    public int Minutes { get; } = minutes;

    public CountdownViewModel Owner { get; } = owner;

    public string Label => $"{Minutes} min";
}

/// <summary>
/// The toolbar's temporizador: a countdown for sermons, games and the like. While it counts it takes the TV
/// (big digits over the background) unless the screen is cleared or the operator hides it; the lyrics come
/// back when it stops. It makes no sound.
/// </summary>
public sealed partial class CountdownViewModel : ObservableObject, IDisposable
{
    private readonly CountdownTimer _timer = new(() => DateTimeOffset.UtcNow);
    private readonly Action _changed;
    private CancellationTokenSource? _loop;
    private string _lastText = string.Empty;

    /// <param name="changed">Called whenever what the TV should show may have changed.</param>
    public CountdownViewModel(Action changed)
    {
        _changed = changed;
        Presets = new[] { 5, 10, 15, 20, 30, 45, 60 }.Select(m => new CountdownPresetViewModel(m, this)).ToList();
    }

    public IReadOnlyList<CountdownPresetViewModel> Presets { get; }

    [ObservableProperty]
    public partial double CustomMinutes { get; set; } = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TvButtonText), nameof(TvContent))]
    public partial bool ShowsOnTv { get; set; } = true;

    public bool IsActive => _timer.IsActive;

    public bool IsIdle => !_timer.IsActive;

    public bool IsFinished => _timer.IsFinished;

    public bool IsPaused => _timer.IsPaused;

    /// <summary>Pausing makes sense while counting or paused, not once it ended.</summary>
    public bool CanPause => _timer.IsRunning || _timer.IsPaused;

    public string RemainingText => CountdownTimer.Format(_timer.Remaining);

    public string ButtonText => IsActive ? $"Temporizador · {RemainingText}" : "Temporizador";

    public string StatusText => IsFinished ? "¡Tiempo terminado!" : IsPaused ? "En pausa" : "Tiempo restante";

    public string PauseButtonText => IsPaused ? "Reanudar" : "Pausar";

    public string TvButtonText => ShowsOnTv ? "Ocultar del TV" : "Mostrar en el TV";

    /// <summary>What the TV shows in place of the slide, or null when the timer is idle or hidden from the TV.</summary>
    public TimerContent? TvContent => IsActive && ShowsOnTv ? new TimerContent(RemainingText, IsFinished) : null;

    [RelayCommand]
    private void Start(int minutes)
    {
        if (minutes < 1)
        {
            return;
        }

        _timer.Start(TimeSpan.FromMinutes(minutes));
        ShowsOnTv = true;
        BeginLoop();
        Refresh();
    }

    [RelayCommand]
    private void StartCustom()
    {
        // NumberBox reports NaN when the field is empty or not a number.
        if (double.IsNaN(CustomMinutes) || CustomMinutes < 1)
        {
            return;
        }

        Start((int)Math.Min(CustomMinutes, CountdownTimer.MaxMinutes));
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (_timer.IsPaused)
        {
            _timer.Resume();
            BeginLoop();
        }
        else
        {
            _timer.Pause();
        }

        Refresh();
    }

    [RelayCommand]
    private void AddMinute()
    {
        _timer.Add(TimeSpan.FromMinutes(1));
        BeginLoop();
        Refresh();
    }

    [RelayCommand]
    private void ToggleTv()
    {
        ShowsOnTv = !ShowsOnTv;
        Refresh();
    }

    [RelayCommand]
    private void Stop()
    {
        _timer.Stop();
        CancelLoop();
        Refresh();
    }

    public void Dispose()
    {
        _timer.Stop();
        CancelLoop();
    }

    private void BeginLoop()
    {
        if (!_timer.IsRunning || _loop is not null)
        {
            return;
        }

        var source = _loop = new CancellationTokenSource();
        _ = RunAsync(source);
    }

    private async Task RunAsync(CancellationTokenSource source)
    {
        try
        {
            while (_timer.IsRunning)
            {
                await Task.Delay(200, source.Token);
                var finished = _timer.Tick();
                if (finished || RemainingText != _lastText)
                {
                    Refresh();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_loop, source))
            {
                _loop = null;
            }
        }
    }

    private void CancelLoop()
    {
        _loop?.Cancel();
        _loop = null;
    }

    private void Refresh()
    {
        _lastText = RemainingText;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(TvContent));
        _changed();
    }
}
