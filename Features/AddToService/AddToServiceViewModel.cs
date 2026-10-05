using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Media;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Features.Bible;

namespace Iris.Features.AddToService;

/// <summary>A selectable library entry (lyrics/music rows, image/video tiles).</summary>
public sealed partial class LibraryEntryViewModel : ObservableObject
{
    private Func<ServiceItem> _toServiceItem;
    private MediaAsset? _asset;

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

    public string? Copyright { get; private init; }

    public bool HasCopyright => !string.IsNullOrEmpty(Copyright);

    public string? Duration { get; private init; }

    public IReadOnlyList<string> Artwork { get; private init; } = ["#15151F", "#07070B"];

    public Guid? MediaId => _asset?.Id;

    /// <summary>Media that is not on this PC yet cannot be added: its cell shows the download instead.</summary>
    public bool IsAvailable => _asset?.IsAvailable ?? true;

    public bool IsUnavailable => !IsAvailable;

    public string StatusText => _asset?.Availability switch
    {
        MediaAvailability.Downloading => $"Descargando… {(int)Math.Round(_asset.Progress * 100)} %",
        MediaAvailability.Failed => "No se pudo descargar",
        MediaAvailability.NotDownloaded => "En espera de descarga",
        _ => string.Empty,
    };

    public double ProgressPercent => (_asset?.Progress ?? 0) * 100;

    public bool IsDownloading => _asset?.Availability == MediaAvailability.Downloading;

    /// <summary>Takes the latest state of the file (progress, ready, failed).</summary>
    public void Apply(MediaAsset asset)
    {
        _asset = asset;
        _toServiceItem = () => ServiceItemFactory.FromMedia(asset);
        OnPropertyChanged(string.Empty);
    }

    public bool HasFirstLine => !string.IsNullOrEmpty(FirstLine);

    public bool HasDuration => !string.IsNullOrEmpty(Duration);

    public bool IsVideo => Kind == ServiceItemKind.Video;

    /// <summary>Tiles reuse the projection renderer: the artwork gradient in 16:9.</summary>
    public ProjectionFrame ThumbnailFrame => _asset switch
    {
        { Kind: MediaKind.Video } video => new ProjectionFrame(null, new VideoContent(Title, video.Duration ?? TimeSpan.Zero, video.LocalPath)),
        { } image => new ProjectionFrame(null, new ImageContent(Title, Artwork, image.LocalPath)),
        _ => new ProjectionFrame(null, new ImageContent(Title, Artwork)),
    };

    public string SearchKey { get; private init; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public ServiceItem ToServiceItem() => _toServiceItem();

    public static LibraryEntryViewModel FromLyrics(LyricSheet sheet, AddToServiceViewModel owner) =>
        new(ServiceItemKind.Song, sheet.Title, sheet.Author, () => ServiceItemFactory.FromLyrics(sheet), owner)
        {
            FirstLine = sheet.FirstLine,
            Copyright = sheet.Copyright,
            // Title, author and the text of every section, without accents or case (api-contract §10).
            SearchKey = BiblePickerViewModel.Normalize($"{sheet.Title} {sheet.Author} {string.Join(' ', sheet.Sections.Select(s => (s.Content as TextContent)?.Body))}"),
        };

    public static LibraryEntryViewModel FromMedia(MediaAsset asset, AddToServiceViewModel owner) =>
        FromMediaCore(asset, owner);

    private static LibraryEntryViewModel FromMediaCore(MediaAsset asset, AddToServiceViewModel owner)
    {
        var entry = new LibraryEntryViewModel(asset.Kind switch { MediaKind.Music => ServiceItemKind.Music, MediaKind.Image => ServiceItemKind.Image, _ => ServiceItemKind.Video },
            asset.Title, asset.Subtitle, () => ServiceItemFactory.FromMedia(asset), owner)
        {
            Duration = asset.Duration is { } d ? DurationText.Format(d) : null,
            Artwork = asset.Artwork,
            SearchKey = BiblePickerViewModel.Normalize($"{asset.Title} {asset.Subtitle}"),
        };
        entry._asset = asset;
        return entry;
    }
}

/// <summary>"Agregar al servicio" sheet (IRIS_SPEC §6.4): 4 tabs, search, ordered multi-selection across tabs.</summary>
public sealed partial class AddToServiceViewModel : ObservableObject, IDisposable
{
    private readonly ILibraryRepository _library;
    private readonly IMediaCache? _cache;
    private readonly IUiDispatcher? _ui;
    private bool _refreshScheduled;
    private bool _disposed;
    private readonly Action<IReadOnlyList<ServiceItem>> _onConfirm;
    private readonly List<LibraryEntryViewModel>[] _tabs = [[], [], [], []];
    private readonly List<LibraryEntryViewModel> _selection = [];

    public AddToServiceViewModel(
        ILibraryRepository library,
        Action<IReadOnlyList<ServiceItem>> onConfirm,
        bool includesMultimedia = true,
        IMediaCache? cache = null,
        IUiDispatcher? ui = null)
    {
        _library = library;
        _cache = cache;
        _ui = ui;
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

    private bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    /// <summary>A search with no hits, or an empty library of the current tab.</summary>
    public string EmptyTitle => IsSearching ? "Sin resultados" : TabIndex switch
    {
        0 => "Aún no hay canciones",
        1 => "Aún no hay música",
        2 => "Aún no hay imágenes",
        _ => "Aún no hay videos",
    };

    public string EmptyMessage => IsSearching ? "Prueba con otro título, autor o descripción." : "Agrégalas desde la web de Iris.";

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
        if (_cache is not null)
        {
            _cache.StateChanged += OnFileStateChanged;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_cache is not null)
        {
            _cache.StateChanged -= OnFileStateChanged;
        }
    }

    // Downloads report progress many times a second: refresh the cells at most twice a second.
    private void OnFileStateChanged(object? sender, Guid id)
    {
        if (_ui is null || _refreshScheduled)
        {
            return;
        }

        _refreshScheduled = true;
        _ui.Post(async () =>
        {
            await Task.Delay(500);
            _refreshScheduled = false;
            if (!_disposed)
            {
                await RefreshMediaStatesAsync();
            }
        });
    }

    private async Task RefreshMediaStatesAsync()
    {
        foreach (var (tab, kind) in new[] { (1, MediaKind.Music), (2, MediaKind.Image), (3, MediaKind.Video) })
        {
            var fresh = (await _library.MediaAsync(kind)).ToDictionary(m => m.Id);
            foreach (var entry in _tabs[tab])
            {
                if (entry.MediaId is { } mediaId && fresh.TryGetValue(mediaId, out var asset))
                {
                    entry.Apply(asset);
                }
            }
        }
    }

    partial void OnTabIndexChanged(int value) => Filter();

    partial void OnQueryChanged(string value) => Filter();

    [RelayCommand]
    private void Toggle(LibraryEntryViewModel entry)
    {
        if (!entry.IsAvailable)
        {
            return;
        }

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
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
