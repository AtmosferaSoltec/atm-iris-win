using System;
using Iris.DesignSystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Iris.Features.Services;

public sealed partial class ServiceTypeEditorView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ServiceTypeEditorViewModel), typeof(ServiceTypeEditorView), new PropertyMetadata(null));

    public ServiceTypeEditorView()
    {
        InitializeComponent();
    }

    public ServiceTypeEditorViewModel? ViewModel
    {
        get => (ServiceTypeEditorViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    /// <summary>Empty block name → danger border.</summary>
    public static Brush NameBorder(bool hasError) => IrisTheme.Brush(hasError ? "IrisDangerBrush" : "IrisStrokeBrush");

    // The view model may refuse (it asks before removing blocks), so the switch re-syncs afterwards.
    private void OnTracksTimeToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.SetTracksTime(TracksTimeSwitch.IsOn);
        if (TracksTimeSwitch.IsOn != viewModel.TracksTime)
        {
            TracksTimeSwitch.IsOn = viewModel.TracksTime;
        }
    }

    private void OnAddPersonSubmitted(object? sender, EventArgs e) => ViewModel?.ConfirmAddPersonCommand.Execute(null);
}
