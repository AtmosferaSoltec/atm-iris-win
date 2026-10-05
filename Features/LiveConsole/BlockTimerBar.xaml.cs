using Iris.Core.Timing;
using Iris.DesignSystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Iris.Features.LiveConsole;

public sealed partial class BlockTimerBar : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(BlockTimerViewModel), typeof(BlockTimerBar), new PropertyMetadata(null));

    public BlockTimerBar()
    {
        InitializeComponent();
    }

    public BlockTimerViewModel? ViewModel
    {
        get => (BlockTimerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static TextDecorations Strike(bool isSkipped) => isSkipped ? TextDecorations.Strikethrough : TextDecorations.None;

    /// <summary>Current and finished blocks read normally; pending and skipped ones are tertiary.</summary>
    public static Brush CrumbBrush(bool isCurrent, bool isDone) =>
        IrisTheme.Brush(isCurrent ? "IrisTextPrimaryBrush" : isDone ? "IrisTextSecondaryBrush" : "IrisTextTertiaryBrush");

    public static Brush DetailBrush(bool isOver) => IrisTheme.Brush(isOver ? "IrisDangerBrush" : "IrisTextSecondaryBrush");

    /// <summary>Capsule behind "−21:18" / "+3:10", tinted by the clock state.</summary>
    public static Brush DeltaBackground(ClockState state) => IrisTheme.Tint(IrisTheme.Color(state switch
    {
        ClockState.Warning => "IrisWarningColor",
        ClockState.Over => "IrisDangerColor",
        _ => "IrisTextPrimaryColor",
    }), 0.12);

    private void OnLeaderChosen(object sender, RoutedEventArgs e) => LeaderFlyout.Hide();
}
