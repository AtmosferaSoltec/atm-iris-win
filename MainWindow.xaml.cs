using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Iris.Core.Services;
using Iris.DesignSystem;
using Iris.Features.Auth;
using Iris.Features.Home;
using Iris.Features.LiveConsole;
using Iris.Features.Modules;
using Iris.Features.People;
using Iris.Features.Services;
using Iris.Features.Times;
using Iris.Shell;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics;

namespace Iris;

/// <summary>
/// Hosts the access screen or the live console depending on <see cref="SessionStore.Session"/>,
/// with the content extended into the title bar.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly SessionStore _session = App.GetService<SessionStore>();
    private readonly SignedInNavigator _navigator = App.GetService<SignedInNavigator>();
    private IReadOnlyList<FrameworkElement> _interactive = [];

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = IrisTheme.Color("IrisTextPrimaryColor");
        AppWindow.TitleBar.ButtonInactiveForegroundColor = IrisTheme.Color("IrisTextTertiaryColor");
        AppWindow.TitleBar.ButtonHoverBackgroundColor = IrisTheme.Color("IrisSurfaceRaisedColor");
        AppWindow.TitleBar.ButtonHoverForegroundColor = IrisTheme.Color("IrisTextPrimaryColor");
        AppWindow.TitleBar.ButtonPressedBackgroundColor = IrisTheme.Color("IrisStrokeStrongColor");
        AppWindow.SetIcon("Assets/Iris.ico");

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1100;
            presenter.PreferredMinimumHeight = 720;
        }

        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = new SizeInt32(System.Math.Min(1440, work.Width), System.Math.Min(900, work.Height));
        AppWindow.MoveAndResize(new RectInt32(work.X + ((work.Width - size.Width) / 2), work.Y + ((work.Height - size.Height) / 2), size.Width, size.Height));

        Root.SizeChanged += (_, _) => UpdatePassthroughRegions();
        _session.PropertyChanged += OnSessionChanged;
        _navigator.PropertyChanged += OnRouteChanged;
        Closed += (_, _) => (App.GetService<IDisplayOutputService>() as ProjectionDisplayService)?.Shutdown();
        ShowScreen();
    }

    /// <summary>Right inset (DIPs) taken by the minimize/maximize/close buttons.</summary>
    public double CaptionButtonsInset => AppWindow.TitleBar.RightInset / (Root.XamlRoot?.RasterizationScale ?? 1);

    /// <summary>
    /// Makes <paramref name="dragArea"/> the window's title bar while keeping
    /// <paramref name="interactive"/> elements inside it clickable.
    /// </summary>
    public void ConfigureTitleBar(UIElement dragArea, params FrameworkElement[] interactive)
    {
        SetTitleBar(dragArea);
        _interactive = interactive;
        foreach (var element in interactive)
        {
            element.SizeChanged -= OnInteractiveSizeChanged;
            element.SizeChanged += OnInteractiveSizeChanged;
        }

        UpdatePassthroughRegions();
    }

    private void OnInteractiveSizeChanged(object sender, SizeChangedEventArgs e) => UpdatePassthroughRegions();

    private void UpdatePassthroughRegions()
    {
        if (Root.XamlRoot is null)
        {
            return;
        }

        var scale = Root.XamlRoot.RasterizationScale;
        var rects = _interactive
            .Where(e => e.Visibility == Visibility.Visible && e.ActualWidth > 0 && e.XamlRoot is not null)
            .Select(e =>
            {
                var bounds = e.TransformToVisual(null).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
                return new RectInt32((int)(bounds.X * scale), (int)(bounds.Y * scale), (int)(bounds.Width * scale), (int)(bounds.Height * scale));
            })
            .ToArray();
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects);
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionStore.Session))
        {
            ShowScreen();
        }
    }

    // Signed out, route changes are ignored: the access screen is already showing.
    private void OnRouteChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignedInNavigator.Route) && _session.Session is not null)
        {
            ShowScreen();
        }
    }

    private void OnSplashFinished(object? sender, System.EventArgs e) => Root.Children.Remove(Splash);

    private void ShowScreen()
    {
        _interactive = [];
        var page = _session.Session is null ? typeof(AuthPage) : _navigator.Route switch
        {
            AppRoute.Console => typeof(LiveConsolePage),
            AppRoute.Modules => typeof(ModulesPage),
            AppRoute.Services => typeof(ServiceTypesPage),
            AppRoute.People => typeof(PeoplePage),
            AppRoute.Times => typeof(TimesPage),
            _ => typeof(HomePage),
        };
        RootFrame.Navigate(page, _navigator.Launch, new SuppressNavigationTransitionInfo());
        RootFrame.BackStack.Clear();
        RootFrame.Opacity = 0;
        Motion.Fade(RootFrame, 1, Motion.Smooth);
    }
}
