using System;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Iris.DesignSystem;
using Iris.Features.AddToService;
using Iris.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Iris.Features.LiveConsole;

public sealed partial class LiveConsolePage : Page
{
    private bool _loaded;

    public LiveConsolePage()
    {
        InitializeComponent();
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        RegisterShortcuts();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += (_, _) => ViewModel.Deactivate();
    }

    public LiveConsoleViewModel ViewModel { get; } = App.GetService<LiveConsoleViewModel>();

    public static string LiveStatus(bool isLive) => isLive ? "En vivo" : string.Empty;

    public static string SelectedStatus(bool isSelected) => isSelected ? "Seleccionado" : string.Empty;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Configure(e.Parameter as ConsoleLaunch);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            _loaded = true;
            ViewModel.LoadCommand.Execute(null);
        }
    }

    private void OnServiceItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ServiceItemViewModel item)
        {
            ViewModel.SelectItemCommand.Execute(item);
        }
    }

    private void OnBackgroundChosen(object sender, RoutedEventArgs e) => BackgroundFlyout.Hide();

    // Drop target of the library entries: the whole SERVICIO panel, empty or not. Reordering the list itself carries no payload.
    private void OnServiceDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.ContainsKey(LibraryPanelView.DragKey))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Agregar al servicio";
            e.Handled = true;
        }
    }

    private void OnServiceDrop(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.TryGetValue(LibraryPanelView.DragKey, out var payload) && payload is LibraryEntryViewModel entry)
        {
            entry.Owner.AddCommand.Execute(entry);
            e.Handled = true;
        }
    }

    // Keeps the live card visible in long chapters.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LiveConsoleViewModel.LiveSlideIndex) || ViewModel.LiveSlideIndex < 0)
        {
            return;
        }

        var index = ViewModel.LiveSlideIndex;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (index < ViewModel.Cards.Count && CardsScroller.Visibility == Visibility.Visible)
            {
                CardsRepeater.GetOrCreateElement(index).StartBringIntoView(new BringIntoViewOptions
                {
                    VerticalAlignmentRatio = 0.5,
                    AnimationDesired = Motion.AnimationsEnabled,
                });
            }
        });
    }

    // ===== Keyboard (IRIS_SPEC §12.4) =====

    private void RegisterShortcuts()
    {
        Add(VirtualKey.Left, VirtualKeyModifiers.None, ViewModel.PreviousCommand);
        Add(VirtualKey.Right, VirtualKeyModifiers.None, ViewModel.NextCommand);
        Add(VirtualKey.Space, VirtualKeyModifiers.None, ViewModel.TogglePlayPauseCommand);
        Add(VirtualKey.B, VirtualKeyModifiers.None, ViewModel.ToggleClearScreenCommand);
        Add(VirtualKey.B, VirtualKeyModifiers.Control, ViewModel.PresentBibleCommand);
        Add(VirtualKey.N, VirtualKeyModifiers.Control, new RelayCommand(() => LibraryPane.FocusSearch()));
        Add(VirtualKey.Delete, VirtualKeyModifiers.None, ViewModel.RemoveSelectedCommand);
        Add(VirtualKey.Z, VirtualKeyModifiers.Control, ViewModel.UndoRemovalCommand);
        Add(VirtualKey.D, VirtualKeyModifiers.Control, ViewModel.DuplicateSelectedCommand);
        Add(VirtualKey.Up, VirtualKeyModifiers.Menu, ViewModel.MoveSelectedUpCommand);
        Add(VirtualKey.Down, VirtualKeyModifiers.Menu, ViewModel.MoveSelectedDownCommand);
        Add(VirtualKey.Right, VirtualKeyModifiers.Control, new RelayCommand(() => ViewModel.BlockTimer?.GoToNextBlockCommand.Execute(null)));
        Add(VirtualKey.Enter, VirtualKeyModifiers.Control, new RelayCommand(() => ViewModel.BlockTimer?.RequestFinishCommand.Execute(null)));

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, ICommand command)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                if (CanUseShortcut(modifiers) && command.CanExecute(null))
                {
                    command.Execute(null);
                    args.Handled = true;
                }
            };
            KeyboardAccelerators.Add(accelerator);
        }
    }

    // Sheets own the keyboard while open; plain keys never steal typing from a text field.
    private bool CanUseShortcut(VirtualKeyModifiers modifiers)
    {
        if (ViewModel.IsModalOpen)
        {
            return false;
        }

        if (modifiers == VirtualKeyModifiers.None && XamlRoot is not null)
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot);
            return focused is not (TextBox or PasswordBox or Slider);
        }

        return true;
    }
}
