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

    /// <summary>"‹ Inicio"; hidden when null (Home).</summary>
    public ICommand? BackCommand { get => (ICommand?)GetValue(BackCommandProperty); set => SetValue(BackCommandProperty, value); }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Activate();
        CaptionColumn.Width = new GridLength(App.MainWindow.CaptionButtonsInset + IrisTheme.Double("IrisSpacingXs"));
        App.MainWindow.ConfigureTitleBar(Bar, BackButton, DisplayChip, AccountButton);
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
