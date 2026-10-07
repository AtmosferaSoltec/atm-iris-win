using System;
using Iris.Core.Persistence;
using Iris.Shell;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace Iris.Features.Modules;

public sealed partial class ModulesPage : Page
{
    public ModulesPage()
    {
        InitializeComponent();
    }

    public ModulesViewModel ViewModel { get; } = App.GetService<ModulesViewModel>();

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    /// <summary>Dark text on the accent fill of the chosen size chip; secondary text on the rest.</summary>
    public static Brush ChipForeground(bool isSelected) =>
        Iris.DesignSystem.IrisTheme.Brush(isSelected ? "IrisTextInverseBrush" : "IrisTextSecondaryBrush");

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LoadCommand.Execute(null);
        _watch = App.GetService<DataWatcher>().Watch(() => ViewModel.LoadCommand.Execute(null), StoreChangeKind.Church);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _watch?.Dispose();
    }

    private IDisposable? _watch;
}
