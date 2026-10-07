using System;
using System.Globalization;

namespace Iris.Core.Timing;

/// <summary>
/// A countdown for sermons, games and the like. Pure logic: it measures with the clock it is given, so a UI that
/// ticks late or not at all still shows the right remaining time. Idle → running ⇄ paused → finished.
/// </summary>
public sealed class CountdownTimer(Func<DateTimeOffset> now)
{
    public const int MaxMinutes = 600;

    private TimeSpan _remainingAtStart;
    private DateTimeOffset _startedAt;

    public bool IsRunning { get; private set; }

    public bool IsPaused { get; private set; }

    /// <summary>Counting, paused or finished: anything but idle.</summary>
    public bool IsActive => IsRunning || IsPaused || IsFinished;

    public bool IsFinished => HasStarted && !IsRunning && !IsPaused;

    private bool HasStarted { get; set; }

    public TimeSpan Remaining
    {
        get
        {
            if (!HasStarted)
            {
                return TimeSpan.Zero;
            }

            if (!IsRunning)
            {
                return _remainingAtStart;
            }

            var left = _remainingAtStart - (now() - _startedAt);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Starts over with <paramref name="duration"/> (clamped to 1 s … <see cref="MaxMinutes"/> min).</summary>
    public void Start(TimeSpan duration)
    {
        _remainingAtStart = Clamp(duration);
        _startedAt = now();
        HasStarted = true;
        IsRunning = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _remainingAtStart = Remaining;
        IsRunning = false;
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        _startedAt = now();
        IsRunning = true;
        IsPaused = false;
    }

    /// <summary>Adds time to a running, paused or finished countdown (a finished one starts running again).</summary>
    public void Add(TimeSpan extra)
    {
        if (!HasStarted)
        {
            return;
        }

        var wasRunning = IsRunning;
        var left = Remaining + extra;
        _remainingAtStart = Clamp(left);
        _startedAt = now();
        if (wasRunning || IsFinished)
        {
            IsRunning = true;
            IsPaused = false;
        }
    }

    public void Stop()
    {
        HasStarted = false;
        IsRunning = false;
        IsPaused = false;
        _remainingAtStart = TimeSpan.Zero;
    }

    /// <summary>Marks a running countdown as finished once it reaches zero. Returns true when it just finished.</summary>
    public bool Tick()
    {
        if (IsRunning && Remaining <= TimeSpan.Zero)
        {
            _remainingAtStart = TimeSpan.Zero;
            IsRunning = false;
            return true;
        }

        return false;
    }

    /// <summary>"05:00", "00:09"; "1:05:00" from one hour. Rounds up, so the display never shows 00:00 before the end.</summary>
    public static string Format(TimeSpan remaining)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        var rest = seconds % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{rest:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{rest:00}");
    }

    private static TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1)
        : value > TimeSpan.FromMinutes(MaxMinutes) ? TimeSpan.FromMinutes(MaxMinutes)
        : value;
}
