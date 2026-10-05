using System;
using Iris.DesignSystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Iris.Features.LiveConsole;

public sealed partial class BlockTimerSheets : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(BlockTimerViewModel), typeof(BlockTimerSheets), new PropertyMetadata(null));

    public BlockTimerSheets()
    {
        InitializeComponent();
    }

    public BlockTimerViewModel? ViewModel
    {
        get => (BlockTimerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    public static Brush RowBackground(bool isSelected) => IrisTheme.Brush(isSelected ? "IrisSurfaceRaisedBrush" : "IrisSurfaceBrush");

    private void OnPickerAddSubmitted(object? sender, EventArgs e) => ViewModel?.ResponsiblePicker?.AddPersonCommand.Execute(null);
}
