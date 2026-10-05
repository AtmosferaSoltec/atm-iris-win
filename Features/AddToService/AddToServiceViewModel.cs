using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Features.Bible;

namespace Iris.Features.AddToService;

/// <summary>A selectable library entry (lyrics/music rows, image/video tiles).</summary>
public sealed partial class LibraryEntryViewModel : ObservableObject
{
    private readonly Func<ServiceItem> _toServiceItem;

    private LibraryEntryViewModel(ServiceItemKind kind, string title, string subtitle, Func<ServiceItem> toServiceItem, AddToServiceViewModel owner)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Owner = owner;
        _toServiceItem = toServiceItem;
    }

    public AddToServiceViewModel Owner { get; }

    public ServiceItemKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string? FirstLine { get; private init; }

    public string? Duration { get; private init; }

    public IReadOnlyList<string> Artwork { get; private init; } = ["#15151F", "#07070B"];

    public bool HasFirstLine => !string.IsNullOrEmpty(FirstLine);

    public bool HasDuration => !string.IsNullOrEmpty(Duration);

    public bool IsVideo => Kind == ServiceItemKind.Video;

    /// <summary>Tiles reuse the projection renderer: the artwork gradient in 16:9.</summary>
    public ProjectionFrame ThumbnailFrame => new(null, new ImageContent(Title, Artwork));

    public string SearchKey { get; private init; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ServiceItem ToServiceItem() => _toServiceItem();

    public static LibraryEntryViewModel FromLyrics(LyricSheet sheet, AddToServiceViewModel owner) =>
        new(ServiceItemKind.Song, sheet.Title, sheet.Author, () => ServiceItemFactory.FromLyrics(sheet), owner)
        {
            FirstLine = sheet.FirstLine,
            SearchKey = BiblePickerViewModel.Normalize($"{sheet.Title} {sheet.Author} {sheet.FirstLine}"),
        };

    public static LibraryEntryViewModel FromMedia(MediaAsset asset, AddToServiceViewModel owner) =>
        new(asset.Kind switch { MediaKind.Music => ServiceItemKind.Music, MediaKind.Image => ServiceItemKind.Image, _ => ServiceItemKind.Video },
            asset.Title, asset.Subtitle, () => ServiceItemFactory.FromMedia(asset), owner)
        {
            Duration = asset.Duration is { } d ? DurationText.Format(d) : null,
            Artwork = asset.Artwork,
            SearchKey = BiblePickerViewModel.Normalize($"{asset.Title} {asset.Subtitle}"),
        };
}

/// <summary>"Agregar al servicio" sheet (IRIS_SPEC §6.4): 4 tabs, search, ordered multi-selection across tabs.</summary>
public sealed partial class AddToServiceViewModel : ObservableObject
{
    private readonly ILibraryRepository _library;
    private readonly Action<IReadOnlyList<ServiceItem>> _onConfirm;
    private readonly List<LibraryEntryViewModel>[] _tabs = [[], [], [], []];
    private readonly List<LibraryEntryViewModel> _selection = [];

    public AddToServiceViewModel(ILibraryRepository library, Action<IReadOnlyList<ServiceItem>> onConfirm, bool includesMultimedia = true)
    {
        _library = library;
        _onConfirm = onConfirm;
        IncludesMultimedia = includesMultimedia;
        Tabs = includesMultimedia ? ["Letras", "Música", "Imágenes", "Videos"] : ["Letras"];
    }

    /// <summary>Without the Multimedia module only Letras exists and the segmented control is hidden (§7.8).</summary>
    public bool IncludesMultimedia { get; }

    public string Subtitle => IncludesMultimedia ? "Elige letras, música, imágenes o videos de tu biblioteca." : "Elige letras de tu biblioteca.";

    public IList<string> Tabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRowTab), nameof(IsTileTab))]
    public partial int TabIndex { get; set; }

    /// <summary>Letras and Música render as rows; Imágenes and Videos as tiles.</summary>
    public bool IsRowTab => TabIndex <= 1;

    public bool IsTileTab => !IsRowTab;

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults))]
    public partial bool IsLoading { get; set; } = true;

    public ObservableCollection<LibraryEntryViewModel> Entries { get; } = [];

    public bool HasNoResults => !IsLoading && Entries.Count == 0;

    public int SelectedCount => _selection.Count;

    public string SelectedCountText => $"Seleccionados: {_selection.Count}";

    public bool CanConfirm => _selection.Count > 0;

    public async Task LoadAsync()
    {
        _tabs[0] = (await _library.LyricsAsync()).Select(l => LibraryEntryViewModel.FromLyrics(l, this)).ToList();
        if (IncludesMultimedia)
        {
            var music = _library.MediaAsync(MediaKind.Music);
            var images = _library.MediaAsync(MediaKind.Image);
            var videos = _library.MediaAsync(MediaKind.Video);
            _tabs[1] = (await music).Select(m => LibraryEntryViewModel.FromMedia(m, this)).ToList();
            _tabs[2] = (await images).Select(m => LibraryEntryViewModel.FromMedia(m, this)).ToList();
            _tabs[3] = (await videos).Select(m => LibraryEntryViewModel.FromMedia(m, this)).ToList();
        }
        IsLoading = false;
        Filter();
    }

    partial void OnTabIndexChanged(int value) => Filter();

    partial void OnQueryChanged(string value) => Filter();

    [RelayCommand]
    private void Toggle(LibraryEntryViewModel entry)
    {
        entry.IsSelected = !entry.IsSelected;
        if (entry.IsSelected)
        {
            _selection.Add(entry);
        }
        else
        {
            _selection.Remove(entry);
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(CanConfirm));
    }

    [RelayCommand]
    private void Confirm()
    {
        if (_selection.Count > 0)
        {
            _onConfirm(_selection.Select(e => e.ToServiceItem()).ToList());
        }
    }

    private void Filter()
    {
        var query = BiblePickerViewModel.Normalize(Query);
        Entries.Clear();
        foreach (var entry in _tabs[Math.Clamp(TabIndex, 0, 3)].Where(e => query.Length == 0 || e.SearchKey.Contains(query, StringComparison.Ordinal)))
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(HasNoResults));
    }
}
