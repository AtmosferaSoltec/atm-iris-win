using Iris.Core.Timing;

namespace Iris.Tests;

public sealed class CountdownTimerTests
{
    private DateTimeOffset _now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private CountdownTimer Create() => new(() => _now);

    [Fact]
    public void Counts_down_with_the_clock_it_is_given()
    {
        var timer = Create();
        timer.Start(TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromSeconds(90);

        Assert.True(timer.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(210), timer.Remaining);
    }

    [Fact]
    public void Pause_freezes_and_resume_continues()
    {
        var timer = Create();
        timer.Start(TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(1);
        timer.Pause();
        _now += TimeSpan.FromMinutes(10);

        Assert.True(timer.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(4), timer.Remaining);

        timer.Resume();
        _now += TimeSpan.FromMinutes(1);
        Assert.Equal(TimeSpan.FromMinutes(3), timer.Remaining);
    }

    [Fact]
    public void Finishes_once_at_zero()
    {
        var timer = Create();
        timer.Start(TimeSpan.FromSeconds(30));
        _now += TimeSpan.FromSeconds(29);
        Assert.False(timer.Tick());

        _now += TimeSpan.FromSeconds(2);
        Assert.True(timer.Tick());
        Assert.False(timer.Tick());
        Assert.True(timer.IsFinished);
        Assert.True(timer.IsActive);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Fact]
    public void Adding_time_restarts_a_finished_countdown_and_extends_a_running_one()
    {
        var timer = Create();
        timer.Start(TimeSpan.FromMinutes(1));
        _now += TimeSpan.FromSeconds(30);
        timer.Add(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromSeconds(90), timer.Remaining);

        _now += TimeSpan.FromMinutes(5);
        timer.Tick();
        Assert.True(timer.IsFinished);

        timer.Add(TimeSpan.FromMinutes(1));
        Assert.True(timer.IsRunning);
        Assert.False(timer.IsFinished);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.Remaining);
    }

    [Fact]
    public void Stop_returns_to_idle()
    {
        var timer = Create();
        timer.Start(TimeSpan.FromMinutes(5));
        timer.Stop();

        Assert.False(timer.IsActive);
        Assert.Equal(TimeSpan.Zero, timer.Remaining);
    }

    [Fact]
    public void Duration_is_clamped()
    {
        var timer = Create();
        timer.Start(TimeSpan.FromDays(3));
        Assert.Equal(TimeSpan.FromMinutes(CountdownTimer.MaxMinutes), timer.Remaining);

        timer.Start(TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Remaining);
    }

    [Theory]
    [InlineData(300, "05:00")]
    [InlineData(299.2, "05:00")]
    [InlineData(59, "00:59")]
    [InlineData(0.4, "00:01")]
    [InlineData(0, "00:00")]
    [InlineData(3905, "1:05:05")]
    public void Formats_rounding_up(double seconds, string expected) =>
        Assert.Equal(expected, CountdownTimer.Format(TimeSpan.FromSeconds(seconds)));
}
