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

/// <summary>A library entry: a lyric sheet, a music track, an image or a video. Dragged or added with "+" into the service.</summary>
public sealed partial class LibraryEntryViewModel : ObservableObject
{
    private Func<ServiceItem> _toServiceItem;
    private MediaAsset? _asset;

    private LibraryEntryViewModel(ServiceItemKind kind, string title, string subtitle, Func<ServiceItem> toServiceItem, LibraryPanelViewModel owner)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Owner = owner;
        _toServiceItem = toServiceItem;
    }

    public LibraryPanelViewModel Owner { get; }

    public ServiceItemKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string? FirstLine { get; private init; }

    public string? Duration { get; private init; }

    public IReadOnlyList<string> Artwork { get; private init; } = ["#15151F", "#07070B"];

    public Guid? MediaId => _asset?.Id;

    /// <summary>The file is on this PC. Media that is not can still be added: adding it starts the download.</summary>
    public bool IsAvailable => _asset?.IsAvailable ?? true;

    public bool IsUnavailable => !IsAvailable;

    public string StatusText => _asset?.Availability switch
    {
        MediaAvailability.Downloading => $"Descargando… {(int)Math.Round(_asset.Progress * 100)} %",
        MediaAvailability.Failed => "No se pudo descargar",
        MediaAvailability.NotDownloaded => "En la nube",
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

    /// <summary>Images and videos show their picture; lyrics and music show the kind's icon.</summary>
    public bool ShowsThumbnail => Kind is ServiceItemKind.Image or ServiceItemKind.Video;

    public bool ShowsIcon => !ShowsThumbnail;

    /// <summary>Thumbnails reuse the projection renderer: the artwork gradient in 16:9.</summary>
    public ProjectionFrame ThumbnailFrame => _asset switch
    {
        { Kind: MediaKind.Video } video => new ProjectionFrame(null, new VideoContent(Title, video.Duration ?? TimeSpan.Zero, video.LocalPath)),
        { } image => new ProjectionFrame(null, new ImageContent(Title, Artwork, image.LocalPath)),
        _ => new ProjectionFrame(null, new ImageContent(Title, Artwork)),
    };

    public string SearchKey { get; private init; } = string.Empty;

    public ServiceItem ToServiceItem() => _toServiceItem();

    public static LibraryEntryViewModel FromLyrics(LyricSheet sheet, LibraryPanelViewModel owner) =>
        new(ServiceItemKind.Song, sheet.Title, sheet.Author, () => ServiceItemFactory.FromLyrics(sheet), owner)
        {
            FirstLine = sheet.FirstLine,
            // Title, author and the text of every section, without accents or case (api-contract §10).
            SearchKey = BiblePickerViewModel.Normalize($"{sheet.Title} {sheet.Author} {string.Join(' ', sheet.Sections.Select(s => (s.Content as TextContent)?.Body))}"),
        };

    public static LibraryEntryViewModel FromMedia(MediaAsset asset, LibraryPanelViewModel owner)
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

/// <summary>
/// The console's library panel (IRIS_SPEC §6.4): Letras · Música · Multimedia tabs and a search box. Entries are dragged
/// into the service list, or added with "+" / double click (the keyboard and mouse-less way).
/// </summary>
public sealed partial class LibraryPanelViewModel : ObservableObject, IDisposable
{
    public const int LyricsTab = 0;
    public const int MusicTab = 1;
    public const int MultimediaTab = 2;

    private readonly ILibraryRepository _library;
    private readonly IMediaCache? _cache;
    private readonly IUiDispatcher? _ui;
    private readonly Action<ServiceItem> _onAdd;
    private readonly List<LibraryEntryViewModel>[] _tabs = [[], [], []];
    private bool _refreshScheduled;
    private bool _disposed;

    public LibraryPanelViewModel(
        ILibraryRepository library,
        Action<ServiceItem> onAdd,
        bool includesMultimedia = true,
        IMediaCache? cache = null,
        IUiDispatcher? ui = null)
    {
        _library = library;
        _cache = cache;
        _ui = ui;
        _onAdd = onAdd;
        IncludesMultimedia = includesMultimedia;
        Tabs = includesMultimedia ? ["Letras", "Música", "Multimedia"] : ["Letras"];
    }

    /// <summary>Without the Multimedia module only Letras exists and the segmented control is hidden (§7.8).</summary>
    public bool IncludesMultimedia { get; }

    public IList<string> Tabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsMusicHint), nameof(ShowsMultimediaHint))]
    public partial int TabIndex { get; set; }

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
        LyricsTab => "Aún no hay canciones",
        MusicTab => "Aún no hay música",
        _ => "Aún no hay multimedia",
    };

    public string EmptyMessage => IsSearching ? "Prueba con otro título, autor o descripción." : TabIndex switch
    {
        LyricsTab => "Agrégalas desde la web de Iris.",
        MusicTab => "Sube tus pistas (MP3, M4A, WAV…) desde la web de Iris, en Música.",
        _ => "Súbelas desde la web de Iris, en Multimedia.",
    };

    /// <summary>Música: where the tracks come from and when they reach this PC.</summary>
    public bool ShowsMusicHint => TabIndex == MusicTab;

    public bool ShowsMultimediaHint => TabIndex == MultimediaTab;

    public async Task LoadAsync()
    {
        _tabs[LyricsTab] = (await _library.LyricsAsync()).Select(l => LibraryEntryViewModel.FromLyrics(l, this)).ToList();
        if (IncludesMultimedia)
        {
            var music = _library.MediaAsync(MediaKind.Music);
            var images = _library.MediaAsync(MediaKind.Image);
            var videos = _library.MediaAsync(MediaKind.Video);
            _tabs[MusicTab] = (await music).Select(m => LibraryEntryViewModel.FromMedia(m, this)).ToList();
            // Backgrounds belong to the background picker, not to the library (the web's Fondos section).
            _tabs[MultimediaTab] = (await images).Where(m => !m.IsBackground)
                .Concat((await videos).Where(m => !m.IsBackground))
                .Select(m => LibraryEntryViewModel.FromMedia(m, this))
                .ToList();
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
        var fresh = new Dictionary<Guid, MediaAsset>();
        foreach (var kind in new[] { MediaKind.Music, MediaKind.Image, MediaKind.Video })
        {
            foreach (var asset in await _library.MediaAsync(kind))
            {
                fresh[asset.Id] = asset;
            }
        }

        foreach (var entry in _tabs.SelectMany(t => t))
        {
            if (entry.MediaId is { } mediaId && fresh.TryGetValue(mediaId, out var asset))
            {
                entry.Apply(asset);
            }
        }
    }

    partial void OnTabIndexChanged(int value) => Filter();

    partial void OnQueryChanged(string value) => Filter();

    /// <summary>"+" / double click / a drop on the service list: puts the entry at the end of the service.</summary>
    [RelayCommand]
    private void Add(LibraryEntryViewModel? entry)
    {
        if (entry is not null)
        {
            _onAdd(entry.ToServiceItem());
        }
    }

    private void Filter()
    {
        var query = BiblePickerViewModel.Normalize(Query);
        Entries.Clear();
        foreach (var entry in _tabs[Math.Clamp(TabIndex, 0, _tabs.Length - 1)].Where(e => query.Length == 0 || e.SearchKey.Contains(query, StringComparison.Ordinal)))
        {
            Entries.Add(entry);
        }

        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}
