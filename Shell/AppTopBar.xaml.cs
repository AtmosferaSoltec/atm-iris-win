using System.Windows.Input;
using Iris.DesignSystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iris.Shell;

public sealed partial class AppTopBar : UserControl
{
    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(nameof(BackCommand), typeof(ICommand), typeof(AppTopBar), new PropertyMetadata(null, (d, _) => ((AppTopBar)d).Apply()));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(AppTopBar), new PropertyMetadata(null, (d, _) => ((AppTopBar)d).Apply()));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(AppTopBar), new PropertyMetadata(null, (d, _) => ((AppTopBar)d).Apply()));

    public AppTopBar()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Apply();
    }

    public TopBarViewModel ViewModel { get; } = App.GetService<TopBarViewModel>();

    public SyncIndicatorViewModel Sync { get; } = App.GetService<SyncIndicatorViewModel>();

    /// <summary>"‹ Inicio"; hidden when null (Home).</summary>
    public ICommand? BackCommand { get => (ICommand?)GetValue(BackCommandProperty); set => SetValue(BackCommandProperty, value); }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Activate();
        Sync.Activate();
        CaptionColumn.Width = new GridLength(App.MainWindow.CaptionButtonsInset + IrisTheme.Double("IrisSpacingXs"));
        App.MainWindow.ConfigureTitleBar(Bar, BackButton, SyncChip, DisplayChip, AccountButton);
    }

    // With more than one secondary monitor the chip opens a menu to pick the TV.
    private void OnDisplayChipTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (!ViewModel.HasMultipleDisplays)
        {
            return;
        }

        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var display in ViewModel.Displays)
        {
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = $"{display.Name} · {display.Resolution}",
                Command = ViewModel.ChooseDisplayCommand,
                CommandParameter = display,
                Icon = display.Id == ViewModel.CurrentDisplayId ? new FontIcon { Glyph = Glyphs.CheckMark } : null,
            });
        }

        menu.ShowAt(DisplayChip);
        e.Handled = true;
    }

    // The church list is data: the submenu is filled each time the menu opens.
    private void OnAccountFlyoutOpening(object? sender, object e)
    {
        ChurchSwitchItem.Items.Clear();
        var show = ViewModel.HasMultipleChurches;
        ChurchSwitchItem.Visibility = ChurchSwitchSeparator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        foreach (var church in ViewModel.Churches)
        {
            var isCurrent = church.Id == ViewModel.CurrentChurchId;
            ChurchSwitchItem.Items.Add(new MenuFlyoutItem
            {
                Text = church.Name,
                Command = ViewModel.SwitchChurchCommand,
                CommandParameter = church,
                Icon = isCurrent ? new FontIcon { Glyph = Glyphs.CheckMark } : null,
            });
        }
    }

    private void Apply()
    {
        BackButton.Visibility = BackCommand is null ? Visibility.Collapsed : Visibility.Visible;
        var hasTitle = !string.IsNullOrEmpty(Title);
        TitleSeparator.Visibility = TitleStack.Visibility = hasTitle ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = Title ?? string.Empty;
        SubtitleText.Text = Subtitle ?? string.Empty;
        SubtitleText.Visibility = string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }
}
