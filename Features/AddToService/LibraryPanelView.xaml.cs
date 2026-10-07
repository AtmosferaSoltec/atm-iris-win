using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Iris.Features.AddToService;

public sealed partial class LibraryPanelView : UserControl
{
    /// <summary>Key of the in-process drag payload: the <see cref="LibraryEntryViewModel"/> being dragged to the service list.</summary>
    public const string DragKey = "IrisLibraryEntry";

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(LibraryPanelViewModel), typeof(LibraryPanelView), new PropertyMetadata(null));

    public LibraryPanelView()
    {
        InitializeComponent();
    }

    public LibraryPanelViewModel? ViewModel
    {
        get => (LibraryPanelViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Ctrl+N: jump to the search box.</summary>
    public void FocusSearch() => SearchField.Focus(FocusState.Programmatic);

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 1 && e.Items[0] is LibraryEntryViewModel entry)
        {
            e.Data.Properties[DragKey] = entry;
            e.Data.SetText(entry.Title);
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
        else
        {
            e.Cancel = true;
        }
    }

    private void OnEntryDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryEntryViewModel entry)
        {
            entry.Owner.AddCommand.Execute(entry);
        }
    }
}
