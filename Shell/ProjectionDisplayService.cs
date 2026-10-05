using Iris.Core.Models;
using Iris.Core.Services;
using Microsoft.UI.Windowing;

namespace Iris.Shell;

/// <summary>
/// Sends frames to a borderless full-screen <see cref="ProjectionWindow"/> on the first
/// non-primary monitor (IRIS_SPEC §12.6). With a single monitor there is no TV.
/// </summary>
public sealed class ProjectionDisplayService : IDisplayOutputService
{
    private ProjectionWindow? _window;
    private ExternalDisplay? _display;
    private bool _probed;

    public ExternalDisplay? ConnectedDisplay()
    {
        EnsureWindow();
        return _display;
    }

    public void Present(ProjectionFrame frame)
    {
        EnsureWindow();
        if (_window is not null)
        {
            _window.Canvas.Frame = frame;
        }
    }

    public void Shutdown()
    {
        _window?.Close();
        _window = null;
    }

    private void EnsureWindow()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;
        var primary = DisplayArea.Primary;
        var areas = DisplayArea.FindAll();
        DisplayArea? secondary = null;
        for (var i = 0; i < areas.Count; i++)
        {
            if (areas[i].DisplayId.Value != primary.DisplayId.Value)
            {
                secondary = areas[i];
                break;
            }
        }

        if (secondary is null)
        {
            return;
        }

        var bounds = secondary.OuterBounds;
        _display = new ExternalDisplay("Sala principal", $"{bounds.Width} × {bounds.Height}");
        _window = new ProjectionWindow(secondary);
    }
}
