using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Shell.Platform;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace Iris.Shell;

/// <summary>
/// Sends frames to a borderless full-screen <see cref="ProjectionWindow"/> on a secondary monitor (IRIS_SPEC §12.6) and
/// follows monitors as they come and go: a <see cref="DisplayAreaWatcher"/> reports plugging and unplugging, the window is
/// created on the new monitor with the last frame already on it, and closed when the monitor disappears — the console is
/// never affected. With a single monitor there is no TV.
/// </summary>
public sealed class ProjectionDisplayService(IUiDispatcher ui) : IDisplayOutputService
{
    private readonly object _gate = new();
    private DisplayAreaWatcher? _watcher;
    private ProjectionWindow? _window;
    private IReadOnlyList<(ExternalDisplay Info, DisplayArea Area)> _secondaries = [];
    private ExternalDisplay? _current;
    private string? _chosenId;
    private ProjectionFrame _lastFrame = ProjectionFrame.Black;
    private bool _refreshQueued;

    public event EventHandler? DisplayChanged;

    public ExternalDisplay? ConnectedDisplay()
    {
        EnsureStarted();
        return _current;
    }

    public IReadOnlyList<ExternalDisplay> Displays()
    {
        EnsureStarted();
        return _secondaries.Select(s => s.Info).ToList();
    }

    public void Choose(ExternalDisplay display)
    {
        _chosenId = display.Id;
        Refresh();
    }

    public void Present(ProjectionFrame frame)
    {
        EnsureStarted();
        _lastFrame = frame;
        if (_window is not null)
        {
            _window.Canvas.Frame = frame;
        }
    }

    public void Shutdown()
    {
        if (_watcher is { } watcher)
        {
            watcher.Added -= OnWatcherChanged;
            watcher.Removed -= OnWatcherChanged;
            watcher.Updated -= OnWatcherChanged;
            watcher.Stop();
            _watcher = null;
        }

        CloseWindow();
    }

    private void EnsureStarted()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = DisplayArea.CreateWatcher();
        _watcher.Added += OnWatcherChanged;
        _watcher.Removed += OnWatcherChanged;
        _watcher.Updated += OnWatcherChanged;
        _watcher.Start();
        Refresh();
    }

    // Plugging a monitor raises several events in a row: coalesce them into one refresh on the UI thread.
    private void OnWatcherChanged(DisplayAreaWatcher sender, object args)
    {
        lock (_gate)
        {
            if (_refreshQueued)
            {
                return;
            }

            _refreshQueued = true;
        }

        ui.Post(() =>
        {
            lock (_gate)
            {
                _refreshQueued = false;
            }

            Refresh();
        });
    }

    private void Refresh()
    {
        var primary = DisplayArea.Primary;
        var areas = DisplayArea.FindAll();
        var secondaries = new List<(ExternalDisplay, DisplayArea)>();
        var index = 2;
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.DisplayId.Value == primary.DisplayId.Value)
            {
                continue;
            }

            var bounds = area.OuterBounds;
            secondaries.Add((new ExternalDisplay(FriendlyName(area, index), $"{bounds.Width} × {bounds.Height}", area.DisplayId.Value.ToString()), area));
            index++;
        }

        _secondaries = secondaries;
        var target = secondaries.FirstOrDefault(s => s.Item1.Id == _chosenId);
        if (target.Item1 is null && secondaries.Count > 0)
        {
            target = secondaries[0];
        }

        var changed = !Equals(_current, target.Item1);
        if (changed)
        {
            CloseWindow();
            _current = target.Item1;
            if (target.Item2 is { } area)
            {
                _window = new ProjectionWindow(area);
                _window.Closed += OnWindowClosed;
                _window.Canvas.Frame = _lastFrame;
            }
        }

        // The list of monitors may have changed even when the TV is the same one (a second monitor appeared).
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosed(object sender, Microsoft.UI.Xaml.WindowEventArgs args)
    {
        // Closed from outside (Alt+F4 on the TV): forget it; the next monitor change opens a new one.
        if (ReferenceEquals(sender, _window))
        {
            _window = null;
        }
    }

    private void CloseWindow()
    {
        if (_window is { } window)
        {
            _window = null;
            window.Closed -= OnWindowClosed;
            window.Close();
        }
    }

    // "Nombre real del monitor si se puede obtener; si no, Pantalla N". Windows reports a generic name for many panels.
    private static string FriendlyName(DisplayArea area, int number)
    {
        var name = NativeMethods.MonitorName(Win32Interop.GetMonitorFromDisplayId(area.DisplayId));
        return string.IsNullOrWhiteSpace(name) || name.Contains("Generic", StringComparison.OrdinalIgnoreCase) || name.Contains("genérico", StringComparison.OrdinalIgnoreCase)
            ? $"Pantalla {number}"
            : name;
    }
}
