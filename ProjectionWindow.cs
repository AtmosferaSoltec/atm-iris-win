using Iris.DesignSystem;
using Iris.Shared.Projection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Iris;

/// <summary>
/// The TV: a borderless full-screen window on the secondary monitor that shows only the live
/// <see cref="ProjectionCanvas"/> — no controls, no chrome (IRIS_SPEC §12.6).
/// </summary>
public sealed partial class ProjectionWindow : Window
{
    public ProjectionWindow(DisplayArea display)
    {
        Title = "Iris · TV";
        Content = new Grid
        {
            Background = IrisTheme.Brush("IrisBlackBrush"),
            Children = { Canvas },
        };

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Move(new PointInt32(display.OuterBounds.X, display.OuterBounds.Y));
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        AppWindow.Show(activateWindow: false);
    }

    public ProjectionCanvas Canvas { get; } = new()
    {
        UsesTransitions = true,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
