using Iris.DesignSystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Iris.Features.Times;

public sealed partial class TimesPage : Page
{
    public TimesPage()
    {
        InitializeComponent();
    }

    public TimesViewModel ViewModel { get; } = App.GetService<TimesViewModel>();

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    /// <summary>Skipped blocks read in the tertiary color.</summary>
    public static Brush BlockNameBrush(bool isSkipped) => IrisTheme.Brush(isSkipped ? "IrisTextTertiaryBrush" : "IrisTextPrimaryBrush");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
    }

    // A filter choice closes whichever filter menu it came from.
    private void OnFilterChosen(object sender, RoutedEventArgs e)
    {
        RecordTypeFlyout.Hide();
        PeriodFlyout.Hide();
        ServiceFlyout.Hide();
        BlockFlyout.Hide();
        PersonFlyout.Hide();
    }
}
