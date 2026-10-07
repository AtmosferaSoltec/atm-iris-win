using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Media;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Core.Sync;
using Iris.Features.AddToService;
using Iris.Features.Bible;
using Iris.Shell;
using SpanishFormat = Iris.Core.Formatting.Spanish;

namespace Iris.Features.LiveConsole;

public enum LoadPhase
{
    Loading,
    Loaded,
    Failed,
}

/// <summary>What is on the TV: an item and the index of its slide.</summary>
public sealed record LiveRef(Guid ItemId, int SlideIndex);

/// <summary>
/// Live console (IRIS_SPEC §6.2, §7). "The list opens, the workspace presents": selecting in the
/// service list never changes the TV; clicking a card or the media stage does. A service started
/// from Home begins empty; the modules it receives stay fixed while it lasts (§7.8).
/// </summary>
public sealed partial class LiveConsoleViewModel : ObservableObject
{
    private static readonly CultureInfo Spanish = SpanishFormat.Culture;

    private readonly IServicePlanRepository _plans;
    private readonly IBackgroundRepository _backgrounds;
    private readonly IBibleRepository _bible;
    private readonly ILibraryRepository _library;
    private readonly IMediaPlaybackService _player;
    private readonly IDisplayOutputService _display;
    private readonly IServiceTypeRepository _types;
    private readonly IPeopleRepository _people;
    private readonly ITimeRecordRepository _records;
    private readonly SignedInNavigator _navigator;
    private readonly ChurchClock _clock;
    private readonly ISyncService _sync;
    private readonly SessionStore _session;
    private readonly IMediaCache _mediaCache;
    private readonly IUiDispatcher _ui;
    private readonly IProjectionSettingsRepository _projection;

    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _undoTimer;
    private Guid? _openItemId;
    private ServiceItem? _presentedScripture;
    private ConsoleLaunch? _launch;

    /// <summary>The latest library state of the files behind the service's media items, by media id.</summary>
    private Dictionary<Guid, MediaAsset> _mediaFiles = [];
    private bool _mediaRefreshScheduled;

    public LiveConsoleViewModel(
        IServicePlanRepository plans,
        IBackgroundRepository backgrounds,
        IBibleRepository bible,
        ILibraryRepository library,
        IMediaPlaybackService player,
        IDisplayOutputService display,
        IServiceTypeRepository types,
        IPeopleRepository people,
        ITimeRecordRepository records,
        SignedInNavigator navigator,
        ISyncService sync,
        SessionStore session,
        IMediaCache mediaCache,
        IUiDispatcher ui,
        ChurchClock clock,
        IProjectionSettingsRepository projection)
    {
        _projection = projection;
        _clock = clock;
        _sync = sync;
        _session = session;
        _mediaCache = mediaCache;
        _ui = ui;
        _plans = plans;
        _backgrounds = backgrounds;
        _bible = bible;
        _library = library;
        _player = player;
        _display = display;
        _types = types;
        _people = people;
        _records = records;
        _navigator = navigator;
        Items.CollectionChanged += (_, _) => Renumber();
    }

    // ===== Top bar =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsLoaded), nameof(IsFailed))]
    public partial LoadPhase Phase { get; set; }

    public bool IsLoading => Phase == LoadPhase.Loading;

    public bool IsLoaded => Phase == LoadPhase.Loaded;

    public bool IsFailed => Phase == LoadPhase.Failed;

    [ObservableProperty]
    public partial string ServiceTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServiceDateText { get; set; } = string.Empty;

    // ===== Modules (§7.8) =====

    public ChurchModules Modules { get; private set; } = ChurchModules.All;

    public bool ShowsBible => Modules.Bible;

    public bool ShowsMultimedia => Modules.Multimedia;

    public string EmptyServiceHint => ShowsMultimedia
        ? "Arrastra aquí letras, música, imágenes o videos desde la biblioteca."
        : "Arrastra aquí letras desde la biblioteca.";

    // ===== Block timer (§6.9) =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBlockTimer))]
    public partial BlockTimerViewModel? BlockTimer { get; set; }

    /// <summary>Only with the time-control module on and a service type that has blocks.</summary>
    public bool ShowsBlockTimer => BlockTimer is not null;

    /// <summary>Sets up the console for the service type started from Home (null = preview service).</summary>
    public void Configure(ConsoleLaunch? launch)
    {
        // The console works with the copy it had when the service began: no pulls until it closes.
        _sync.Suspend();
        _player.ProgressChanged -= OnPlayerProgress;
        _player.Ended -= OnPlayerEnded;
        _player.ProgressChanged += OnPlayerProgress;
        _player.Ended += OnPlayerEnded;
        _mediaCache.StateChanged -= OnMediaFileChanged;
        _mediaCache.StateChanged += OnMediaFileChanged;
        _launch = launch;
        Modules = launch?.Modules ?? ChurchModules.All;
        BlockTimer = launch is { ServiceType: { TracksTime: true } type } && Modules.TimeControl
            ? new BlockTimerViewModel(type, launch.People, _types, _people, _records, _navigator, launch.StartedAt, () => _clock.Now, _session.Can(Permission.ServiceTypesManage), _session.Can(Permission.RecordsWrite), _sync, _ui)
            : null;
        if (BlockTimer is { } timer)
        {
            timer.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BlockTimerViewModel.IsSheetOpen))
                {
                    OnPropertyChanged(nameof(IsModalOpen));
                }
            };
        }

        OnPropertyChanged(nameof(Modules));
        OnPropertyChanged(nameof(ShowsBible));
        OnPropertyChanged(nameof(ShowsMultimedia));
        OnPropertyChanged(nameof(EmptyServiceHint));
        OpenLibrary();
    }

    // ===== Service list =====

    public ObservableCollection<ServiceItemViewModel> Items { get; } = [];

    public string ItemCountText => Items.Count.ToString(Spanish);

    public bool HasItems => Items.Count > 0;

    public bool IsServiceEmpty => IsLoaded && Items.Count == 0;

    public string ClearServiceButtonText => Items.Count == 1 ? "Quitar 1 elemento" : $"Quitar {Items.Count} elementos";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    public partial bool IsConfirmingClear { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UndoMessage), nameof(HasRecentlyRemoved))]
    public partial RemovedItem? RecentlyRemoved { get; set; }

    public bool HasRecentlyRemoved => RecentlyRemoved is not null;

    public string UndoMessage => RecentlyRemoved is null ? string.Empty : $"Se quitó «{RecentlyRemoved.Item.Title}»";

    // ===== TV state =====

    public IReadOnlyList<BackgroundOptionViewModel> Backgrounds { get; private set; } = [];

    [ObservableProperty]
    public partial string? SelectedBackgroundId { get; set; }

    [ObservableProperty]
    public partial LiveRef? Live { get; set; }

    [ObservableProperty]
    public partial bool IsScreenCleared { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayback))]
    public partial PlaybackViewModel? Playback { get; set; }

    public bool HasPlayback => Playback is not null;

    /// <summary>What the TV shows (§7.3): background only when cleared or nothing is live.</summary>
    public ProjectionFrame LiveFrame
    {
        get
        {
            var background = Backgrounds.FirstOrDefault(b => b.Model.Id == SelectedBackgroundId)?.Model;
            if (IsScreenCleared || Live is null || FindItem(Live.ItemId) is not { } item || Live.SlideIndex >= item.Slides.Count)
            {
                return new ProjectionFrame(background, ProjectionContent.Blank);
            }

            return new ProjectionFrame(background, Resolve(item, item.Slides[Live.SlideIndex].Content));
        }
    }

    /// <summary>Toolbar: "Fondo · Aurora", or "Fondo · Negro" with none.</summary>
    public string BackgroundButtonText =>
        $"Fondo · {Backgrounds.FirstOrDefault(b => b.Model.Id == SelectedBackgroundId)?.Name ?? "Negro"}";

    // ===== Workspace =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingScripture))]
    public partial ServiceItem? ScriptureItem { get; set; }

    public bool IsShowingScripture => ScriptureItem is not null;

    public ServiceItem? OpenItem => ScriptureItem ?? Items.FirstOrDefault(i => i.Id == _openItemId)?.Model;

    public bool HasOpenItem => OpenItem is not null;

    public bool IsNothingSelected => IsLoaded && OpenItem is null;

    public ServiceItemKind OpenKind => OpenItem?.Kind ?? ServiceItemKind.Song;

    public string OpenTitle => OpenItem?.Title ?? string.Empty;

    public string OpenSubtitle => OpenItem?.Subtitle ?? string.Empty;

    public bool IsShowingCards => IsLoaded && OpenItem is { IsMedia: false };

    public bool IsShowingMedia => IsLoaded && OpenItem is { IsMedia: true };

    public bool IsEditVisible => HasOpenItem && !IsShowingScripture;

    public ObservableCollection<SlideCardViewModel> Cards { get; } = [];

    /// <summary>Index of the live slide when it belongs to the open item (drives auto-scroll), else -1.</summary>
    public int LiveSlideIndex => OpenItem is { } open && Live is { } live && live.ItemId == open.Id ? live.SlideIndex : -1;

    public bool CanGoPrevious => IsShowingCards && LiveSlideIndex > 0;

    public bool CanGoNext => IsShowingCards && LiveSlideIndex < Cards.Count - 1;

    public ProjectionFrame MediaFrame => OpenItem is { IsMedia: true } item ? new ProjectionFrame(null, Resolve(item, item.Slides[0].Content)) : ProjectionFrame.Black;

    // ----- The open media's file (api-contract §11: music and videos download once added) -----

    /// <summary>Design data and files already on this PC count as ready.</summary>
    private MediaAvailability OpenMediaAvailability => OpenItem?.MediaId is { } id && _mediaFiles.TryGetValue(id, out var asset)
        ? asset.Availability
        : MediaAvailability.Placeholder;

    public bool IsOpenMediaReady => OpenMediaAvailability is MediaAvailability.Placeholder or MediaAvailability.Ready;

    public bool IsOpenMediaFailed => OpenMediaAvailability == MediaAvailability.Failed;

    /// <summary>Still in the cloud or on its way: the stage shows the progress instead of the action.</summary>
    public bool IsOpenMediaDownloading => !IsOpenMediaReady && !IsOpenMediaFailed;

    public string MediaDownloadText => OpenItem?.MediaId is { } id && _mediaFiles.TryGetValue(id, out var asset) && asset.Availability == MediaAvailability.Downloading
        ? $"Descargando… {(int)Math.Round(asset.Progress * 100)} %"
        : "Descargando…";

    public bool IsSelectedMediaActive => OpenItem switch
    {
        { Kind: ServiceItemKind.Image } image => !IsScreenCleared && Live?.ItemId == image.Id,
        { IsMedia: true } media => Playback is { IsPlaying: true } p && p.ItemId == media.Id,
        _ => false,
    };

    public bool CanPresentSelectedMedia => !IsSelectedMediaActive && IsOpenMediaReady;

    public string MediaButtonText => OpenKind == ServiceItemKind.Image
        ? IsSelectedMediaActive ? "En pantalla" : "Mostrar en el TV"
        : IsSelectedMediaActive ? "Reproduciendo" : "Reproducir";

    public string MediaBadgeText => OpenKind == ServiceItemKind.Image ? "EN PANTALLA" : "REPRODUCIENDO";

    public string MediaHint => IsOpenMediaFailed
        ? "No se pudo descargar. Revisa la conexión a internet."
        : IsOpenMediaDownloading
            ? "Se descarga una sola vez y queda guardada en esta computadora."
            : OpenKind switch
    {
        ServiceItemKind.Music => "La música suena en el salón; el TV no cambia. Contrólala desde el reproductor.",
        ServiceItemKind.Video => "El video se muestra en el TV. Contrólalo desde el reproductor bajo la pantalla en vivo.",
        _ => "La imagen se muestra a pantalla completa en el TV.",
    };

    // ===== Sheets =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBibleOpen), nameof(IsModalOpen))]
    public partial BiblePickerViewModel? BiblePicker { get; set; }

    public bool IsBibleOpen => BiblePicker is not null;

    // The library panel is always on screen (not a sheet): entries are dragged to the service or added with "+".
    [ObservableProperty]
    public partial LibraryPanelViewModel? Library { get; set; }

    public bool IsModalOpen => IsConfirmingClear || IsBibleOpen || BlockTimer?.IsSheetOpen == true;

    // ===== Lifecycle =====

    [RelayCommand]
    private async Task LoadAsync()
    {
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
        Phase = LoadPhase.Loading;
        try
        {
            var backgrounds = await _backgrounds.BackgroundsAsync();
            Backgrounds = backgrounds.Select(b => new BackgroundOptionViewModel(b, this)).ToList();
            OnPropertyChanged(nameof(Backgrounds));

            // How the lyrics look (api-contract §6): every surface, the TV included, follows it.
            var typography = await LoadTypographyAsync();
            Iris.Shared.Projection.ProjectionTypography.Set(typography);
            Items.Clear();

            if (_launch is { ServiceType: { } type } launch)
            {
                // A new service starts as the church set it up in Proyección: its default background if it still
                // exists, pure black otherwise — never the first background by chance.
                SelectBackgroundCore(Backgrounds.FirstOrDefault(b => b.Model.Id == typography.DefaultBackgroundId)?.Model.Id);
                // Started from Home (§6.2): empty list, nothing live, first background; the date is the start time.
                ServiceTitle = type.Name;
                ServiceDateText = $"{SpanishFormat.DayAndMonth(launch.StartedAt)} · {SpanishFormat.Time(launch.StartedAt)}";
                Phase = LoadPhase.Loaded;
            }
            else
            {
                // Preview service (§7.7): "Sublime gracia" open, its 2nd stanza on the TV, "Aurora" background.
                SelectBackgroundCore(Backgrounds.FirstOrDefault()?.Model.Id);
                var plan = await _plans.CurrentServiceAsync();
                ServiceTitle = plan.Title;
                ServiceDateText = plan.Date.ToString("dddd, d 'de' MMMM · HH:mm", Spanish);
                foreach (var item in plan.Items.Where(i => ShowsMultimedia || !i.IsMedia))
                {
                    Items.Add(new ServiceItemViewModel(item, this));
                }

                Phase = LoadPhase.Loaded;
                if (Items.Count > 1)
                {
                    Open(Items[1].Id);
                    if (Cards.Count > 1)
                    {
                        GoLive(Cards[1]);
                    }
                }
            }

            Changed();
        }
        catch (Exception)
        {
            Phase = LoadPhase.Failed;
            Changed();
        }
    }

    /// <summary>A church that never opened Proyección just gets the defaults (system typeface, 88 pt, black).</summary>
    private async Task<ProjectionSettings> LoadTypographyAsync()
    {
        try
        {
            return await _projection.SettingsAsync();
        }
        catch (Exception)
        {
            return ProjectionSettings.Default;
        }
    }

    /// <summary>Stops loops and blanks the TV when the console goes away.</summary>
    public void Deactivate()
    {
        _player.ProgressChanged -= OnPlayerProgress;
        _player.Ended -= OnPlayerEnded;
        _mediaCache.StateChanged -= OnMediaFileChanged;
        CloseLibrary();
        CloseBiblePicker();
        _sync.Resume();
        _lifetime.Cancel();
        _undoTimer?.Cancel();
        BlockTimer?.Stop();
        if (Playback is not null)
        {
            _player.Stop();
            Playback = null;
        }

        _display.Present(ProjectionFrame.Black);
    }

    /// <summary>"‹ Inicio": asks first while the block timer is running (§6.9).</summary>
    [RelayCommand]
    private void RequestExit()
    {
        if (BlockTimer?.RequestExit() == false)
        {
            return;
        }

        _navigator.GoHome();
    }

    // ===== Opening and presenting =====

    [RelayCommand]
    private void SelectItem(ServiceItemViewModel item)
    {
        ScriptureItem = null;
        Open(item.Id);
        Changed();
    }

    [RelayCommand]
    private void GoLive(SlideCardViewModel card)
    {
        if (OpenItem is not { } item)
        {
            return;
        }

        Live = new LiveRef(item.Id, card.Index);
        IsScreenCleared = false;
        PauseVideoIfPlaying();
        Changed();
    }

    [RelayCommand]
    private void PresentSelectedMedia()
    {
        // A file still downloading waits for it (api-contract §11).
        if (OpenItem is not { IsMedia: true } item || IsSelectedMediaActive || !IsOpenMediaReady)
        {
            return;
        }

        switch (item.Kind)
        {
            case ServiceItemKind.Music:
                // The TV does not change; the music plays in the room.
                StartOrResume(item);
                break;
            case ServiceItemKind.Video:
                Live = new LiveRef(item.Id, 0);
                IsScreenCleared = false;
                StartOrResume(item);
                break;
            default:
                Live = new LiveRef(item.Id, 0);
                IsScreenCleared = false;
                PauseVideoIfPlaying();
                break;
        }

        Changed();
    }

    [RelayCommand]
    private void ToggleClearScreen()
    {
        IsScreenCleared = !IsScreenCleared;
        Changed();
    }

    [RelayCommand]
    private void Next() => Step(+1);

    [RelayCommand]
    private void Previous() => Step(-1);

    private void Step(int by)
    {
        if (!IsShowingCards || Cards.Count == 0)
        {
            return;
        }

        var target = LiveSlideIndex < 0 ? 0 : Math.Clamp(LiveSlideIndex + by, 0, Cards.Count - 1);
        if (target != LiveSlideIndex)
        {
            GoLive(Cards[target]);
        }
    }

    [RelayCommand]
    private void SelectBackground(BackgroundOptionViewModel option)
    {
        SelectBackgroundCore(option.Model.Id);
        Changed();
    }

    private void SelectBackgroundCore(string? id)
    {
        SelectedBackgroundId = id;
        foreach (var option in Backgrounds)
        {
            option.IsSelected = option.Model.Id == id;
        }
    }

    // ===== Playback (§7.4) =====

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (Playback is not { } playback)
        {
            return;
        }

        if (playback.IsPlaying)
        {
            playback.IsPlaying = false;
            _player.Pause();
        }
        else
        {
            if (playback.IsVideo)
            {
                // A paused video resumes on the TV.
                Live = new LiveRef(playback.ItemId, 0);
                IsScreenCleared = false;
            }

            playback.IsPlaying = true;
            _player.Resume();
            if (!playback.IsReal)
            {
                RunPlaybackClock(playback);
            }
        }

        Changed();
    }

    [RelayCommand]
    private void RestartPlayback()
    {
        if (Playback is { } playback)
        {
            playback.Rewind();
            _player.Seek(0);
        }
    }

    [RelayCommand]
    private void ToggleLooping()
    {
        if (Playback is { } playback)
        {
            playback.IsLooping = !playback.IsLooping;
            _player.SetLooping(playback.IsLooping);
        }
    }

    [RelayCommand]
    private void StopPlayback()
    {
        StopPlaybackCore();
        Changed();
    }

    private void StopPlaybackCore()
    {
        if (Playback is not { } playback)
        {
            return;
        }

        // Stopping a video that is on the TV leaves only the background.
        if (playback.IsVideo && Live?.ItemId == playback.ItemId)
        {
            Live = null;
        }

        _player.Stop();
        Playback = null;
    }

    private void StartOrResume(ServiceItem item)
    {
        if (Playback is { } current && current.ItemId == item.Id)
        {
            // Presenting the loaded media again resumes it, never restarts.
            if (!current.IsPlaying)
            {
                current.IsPlaying = true;
                _player.Resume();
                if (!current.IsReal)
                {
                    RunPlaybackClock(current);
                }
            }

            return;
        }

        // Only one playback at a time. Keep the TV if the new item is the video being put live.
        var keepLive = Live;
        StopPlaybackCore();
        if (keepLive?.ItemId == item.Id)
        {
            Live = keepLive;
        }

        var duration = item.Slides[0].Content switch
        {
            AudioContent audio => audio.Duration.TotalSeconds,
            VideoContent video => video.Duration.TotalSeconds,
            _ => 0,
        };
        var content = Resolve(item, item.Slides[0].Content);
        var request = new PlaybackRequest(
            item.Id,
            item.Kind == ServiceItemKind.Video,
            item.Title,
            content switch { AudioContent audio => audio.LocalPath, VideoContent video => video.LocalPath, _ => null },
            TimeSpan.FromSeconds(duration));
        var playback = new PlaybackViewModel(item, duration, _player) { IsPlaying = true };
        Playback = playback;

        // A cached file plays for real and reports its own time; design data has none, so the console simulates the clock.
        playback.IsReal = _player.Play(request);
        if (!playback.IsReal)
        {
            RunPlaybackClock(playback);
        }
    }

    // ----- Real player events (UI thread) -----

    private void OnPlayerProgress(object? sender, PlaybackProgress progress)
    {
        if (Playback is not { IsReal: true } playback)
        {
            return;
        }

        var wasPlaying = playback.IsPlaying;
        playback.SetFromPlayer(progress.Elapsed, progress.Duration, progress.IsPlaying);

        // The system media controls (or a keyboard media key) can pause and resume too: keep the console in step.
        if (wasPlaying != progress.IsPlaying)
        {
            if (playback.IsVideo && progress.IsPlaying)
            {
                Live = new LiveRef(playback.ItemId, 0);
                IsScreenCleared = false;
            }

            Changed();
        }
    }

    private void OnPlayerEnded(object? sender, EventArgs e)
    {
        if (Playback is { IsReal: true, IsLooping: false })
        {
            StopPlayback();
        }
    }

    private void PauseVideoIfPlaying()
    {
        if (Playback is { IsVideo: true, IsPlaying: true } video)
        {
            video.IsPlaying = false;
            _player.Pause();
        }
    }

    private async void RunPlaybackClock(PlaybackViewModel playback)
    {
        var token = _lifetime.Token;
        try
        {
            while (Playback == playback && playback.IsPlaying && !token.IsCancellationRequested)
            {
                await Task.Delay(500, token);
                if (Playback != playback || !playback.IsPlaying)
                {
                    return;
                }

                playback.Tick(0.5);
                if (playback.Elapsed >= playback.Duration)
                {
                    if (playback.IsLooping)
                    {
                        playback.Rewind();
                    }
                    else
                    {
                        StopPlayback();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ===== Service editing (§7.6) =====

    private void OpenLibrary()
    {
        CloseLibrary();
        var library = new LibraryPanelViewModel(_library, AddLibraryItem, ShowsMultimedia, _mediaCache, _ui);
        Library = library;
        _ = LoadLibraryAsync(library);
    }

    private static async Task LoadLibraryAsync(LibraryPanelViewModel library)
    {
        try
        {
            await library.LoadAsync();
        }
        catch (Exception)
        {
            library.IsLoading = false;
        }
    }

    private void CloseLibrary()
    {
        Library?.Dispose();
        Library = null;
    }

    /// <summary>A library entry dropped on the service (or added with "+"): it goes to the end of the list and opens.</summary>
    private void AddLibraryItem(ServiceItem item)
    {
        IReadOnlyList<ServiceItem> items = [item];
        Items.Add(new ServiceItemViewModel(item, this));

        // Files not on this PC yet start downloading now and stay here afterwards (api-contract §11). Until the
        // library answers they count as on their way, so nothing tries to play a file that is not here.
        var missing = items.Where(i => i.MediaId is not null && i.Slides[0].Content.LocalPath() is null).Select(i => i.MediaId!.Value).ToList();
        if (missing.Count > 0 && !_mediaCache.IsNull)
        {
            foreach (var id in missing)
            {
                _mediaFiles.TryAdd(id, new MediaAsset(id, MediaKind.Image, string.Empty, string.Empty, null, []) { Availability = MediaAvailability.NotDownloaded });
            }

            _ = _library.DownloadAsync(missing);
        }

        ScriptureItem = null;
        Open(items[0].Id);
        Changed();

        _ = RefreshMediaFilesAsync();
    }

    /// <summary>"Reintentar descarga" on the media stage.</summary>
    [RelayCommand]
    private void RetryMediaDownload()
    {
        if (OpenItem?.MediaId is { } id)
        {
            _ = _library.DownloadAsync([id]);
        }
    }

    // Downloads report progress many times a second: refresh at most twice a second.
    private void OnMediaFileChanged(object? sender, Guid id)
    {
        if (_mediaRefreshScheduled)
        {
            return;
        }

        _mediaRefreshScheduled = true;
        _ui.Post(async () =>
        {
            await Task.Delay(500);
            _mediaRefreshScheduled = false;
            await RefreshMediaFilesAsync();
        });
    }

    /// <summary>Reads where each media item's file stands (library state) and redraws what depends on it.</summary>
    private async Task RefreshMediaFilesAsync()
    {
        var ids = Items.Select(i => i.Model.MediaId).OfType<Guid>().ToHashSet();
        if (ids.Count == 0)
        {
            return;
        }

        var files = new Dictionary<Guid, MediaAsset>();
        foreach (var kind in new[] { MediaKind.Music, MediaKind.Image, MediaKind.Video })
        {
            foreach (var asset in await _library.MediaAsync(kind))
            {
                if (ids.Contains(asset.Id))
                {
                    files[asset.Id] = asset;
                }
            }
        }

        // Deleted on the web meanwhile: it can no longer play.
        foreach (var missing in ids.Where(id => !files.ContainsKey(id)))
        {
            files[missing] = new MediaAsset(missing, MediaKind.Image, string.Empty, string.Empty, null, []) { Availability = MediaAvailability.Failed };
        }

        _mediaFiles = files;
        Changed();
    }

    /// <summary>A media slide with the file that is on this PC now (it may have arrived after the item was added).</summary>
    private ProjectionContent Resolve(ServiceItem item, ProjectionContent content) =>
        item.MediaId is { } id && _mediaFiles.TryGetValue(id, out var asset) && asset.Availability == MediaAvailability.Ready
            ? content.WithLocalPath(asset.LocalPath)
            : content;

    [RelayCommand]
    private void RemoveItem(ServiceItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        Items.RemoveAt(index);
        if (Playback?.ItemId == item.Id)
        {
            StopPlaybackCore();
        }

        if (Live?.ItemId == item.Id)
        {
            Live = null;
        }

        if (_openItemId == item.Id)
        {
            Open(Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)].Id);
        }

        ShowUndo(new RemovedItem(item.Model, index));
        Changed();
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (!IsShowingScripture && Items.FirstOrDefault(i => i.Id == _openItemId) is { } item)
        {
            RemoveItem(item);
        }
    }

    [RelayCommand]
    private void UndoRemoval()
    {
        if (RecentlyRemoved is not { } removed)
        {
            return;
        }

        _undoTimer?.Cancel();
        RecentlyRemoved = null;
        Items.Insert(Math.Min(removed.Index, Items.Count), new ServiceItemViewModel(removed.Item, this));
        ScriptureItem = null;
        Open(removed.Item.Id);
        Changed();
    }

    [RelayCommand]
    private void DuplicateItem(ServiceItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        var copy = item.Model.Duplicate();
        Items.Insert(index + 1, new ServiceItemViewModel(copy, this));
        ScriptureItem = null;
        Open(copy.Id);
        Changed();
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (!IsShowingScripture && Items.FirstOrDefault(i => i.Id == _openItemId) is { } item)
        {
            DuplicateItem(item);
        }
    }

    [RelayCommand]
    private void MoveItemUp(ServiceItemViewModel item) => MoveItem(item, -1);

    [RelayCommand]
    private void MoveItemDown(ServiceItemViewModel item) => MoveItem(item, +1);

    [RelayCommand]
    private void MoveSelectedUp() => MoveSelected(-1);

    [RelayCommand]
    private void MoveSelectedDown() => MoveSelected(+1);

    private void MoveSelected(int by)
    {
        if (!IsShowingScripture && Items.FirstOrDefault(i => i.Id == _openItemId) is { } item)
        {
            MoveItem(item, by);
        }
    }

    private void MoveItem(ServiceItemViewModel item, int by)
    {
        var index = Items.IndexOf(item);
        var target = index + by;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Items.Move(index, target);
    }

    [RelayCommand]
    private void RequestClearService()
    {
        if (Items.Count > 0)
        {
            OnPropertyChanged(nameof(ClearServiceButtonText));
            IsConfirmingClear = true;
        }
    }

    [RelayCommand]
    private void CancelClearService() => IsConfirmingClear = false;

    [RelayCommand]
    private void ClearService()
    {
        IsConfirmingClear = false;
        var ids = Items.Select(i => i.Id).ToHashSet();
        if (Playback is { } playback && ids.Contains(playback.ItemId))
        {
            StopPlaybackCore();
        }

        if (Live is { } live && ids.Contains(live.ItemId))
        {
            Live = null;
        }

        Items.Clear();
        if (!IsShowingScripture)
        {
            Open(null);
        }

        Changed();
    }

    private void ShowUndo(RemovedItem removed)
    {
        _undoTimer?.Cancel();
        var timer = _undoTimer = new CancellationTokenSource();
        RecentlyRemoved = removed;
        _ = HideUndoLater(timer.Token);

        async Task HideUndoLater(CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                RecentlyRemoved = null;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    // ===== Bible (§6.3, §7.5) =====

    [RelayCommand]
    private void PresentBible()
    {
        // Ctrl+B too: the Bible may be off for this church or for all of Iris (api-contract §6).
        if (!ShowsBible)
        {
            return;
        }

        var picker = new BiblePickerViewModel(_bible, PresentScriptureAsync, _ui);
        BiblePicker = picker;
        _ = picker.LoadAsync();
    }

    [RelayCommand]
    private void CloseBible() => CloseBiblePicker();

    private void CloseBiblePicker()
    {
        BiblePicker?.Dispose();
        BiblePicker = null;
    }

    /// <summary>Opens the whole chapter as a temporary item (not added to the service) and sends the verse live.</summary>
    private async Task PresentScriptureAsync(BibleBook book, int chapter, int verse)
    {
        var verses = await _bible.VersesAsync(book.Id, chapter);
        CloseBiblePicker();
        ScriptureItem = _presentedScripture = ServiceItemFactory.FromScripture(book, chapter, verses, _bible.TranslationName);
        RebuildCards();
        var index = Math.Clamp(verse - 1, 0, Cards.Count - 1);
        if (Cards.Count > 0)
        {
            GoLive(Cards[index]);
        }

        Changed();
    }

    // ===== Helpers =====

    // The last presented passage stays resolvable after the workspace moves on, so the TV keeps showing it.
    private ServiceItem? FindItem(Guid id) =>
        _presentedScripture is { } scripture && scripture.Id == id ? scripture : Items.FirstOrDefault(i => i.Id == id)?.Model;

    private void Open(Guid? itemId)
    {
        _openItemId = itemId;
        RebuildCards();
    }

    private void RebuildCards()
    {
        Cards.Clear();
        if (OpenItem is { IsMedia: false } item)
        {
            for (var i = 0; i < item.Slides.Count; i++)
            {
                Cards.Add(new SlideCardViewModel(i, item.Slides[i], this));
            }
        }
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Number = (i + 1).ToString("00", Spanish);
            Items[i].CanMoveUp = i > 0;
            Items[i].CanMoveDown = i < Items.Count - 1;
        }

        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsServiceEmpty));
    }

    /// <summary>Refreshes derived state and pushes the live frame to the TV.</summary>
    private void Changed()
    {
        foreach (var row in Items)
        {
            row.IsSelected = !IsShowingScripture && row.Id == _openItemId;
        }

        var liveIndex = LiveSlideIndex;
        foreach (var card in Cards)
        {
            card.IsLive = card.Index == liveIndex && !IsScreenCleared;
        }

        foreach (var name in DerivedProperties)
        {
            OnPropertyChanged(name);
        }

        _display.Present(LiveFrame);
    }

    private static readonly string[] DerivedProperties =
    [
        nameof(LiveFrame), nameof(OpenItem), nameof(HasOpenItem), nameof(IsNothingSelected), nameof(OpenKind), nameof(OpenTitle),
        nameof(OpenSubtitle), nameof(IsShowingCards), nameof(IsShowingMedia), nameof(IsEditVisible), nameof(LiveSlideIndex),
        nameof(CanGoPrevious), nameof(CanGoNext), nameof(MediaFrame), nameof(IsSelectedMediaActive), nameof(CanPresentSelectedMedia),
        nameof(MediaButtonText), nameof(MediaBadgeText), nameof(MediaHint), nameof(IsServiceEmpty), nameof(ClearServiceButtonText),
        nameof(IsOpenMediaReady), nameof(IsOpenMediaFailed), nameof(IsOpenMediaDownloading), nameof(MediaDownloadText),
        nameof(BackgroundButtonText),
    ];
}
