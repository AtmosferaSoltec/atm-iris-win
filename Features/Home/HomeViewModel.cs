using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Core.Sync;
using Iris.Shell;

namespace Iris.Features.Home;

/// <summary>A service-type pill in the hero (dot + name; selected = white capsule).</summary>
public sealed partial class ServiceTypeOptionViewModel(ServiceType type, HomeViewModel owner) : ObservableObject
{
    public ServiceType Type { get; } = type;

    public HomeViewModel Owner { get; } = owner;

    public string Name => Type.Name;

    public string Color => Type.Color;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>A row of the Servicios tile: color dot, name and "Con tiempos" / "Solo proyección".</summary>
public sealed record ServiceTypeSummary(string Name, string Color, bool TracksTime)
{
    public bool IsProjectionOnly => !TracksTime;
}

/// <summary>A block in the hero's plan: dot, name, suggested leader, "10 min".</summary>
public sealed record BlockPlanRow(int Index, string Name, string Leader, string Minutes)
{
    public bool HasLeader => Leader.Length > 0;
}

public sealed record ModuleStatus(string Name, bool IsOn)
{
    public string StatusText => IsOn ? "Activo" : "Apagado";
}

public sealed record AvatarItem(string Initials, int ColorIndex);

/// <summary>
/// Home (IRIS_SPEC §6.1b). Lives for the whole session; reloads silently every time Home appears
/// so changes made on other screens show up. Keeps the chosen service type if it still exists.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly SessionStore _session;
    private readonly SignedInNavigator _navigator;
    private readonly IModuleSettingsRepository _modules;
    private readonly IServiceTypeRepository _types;
    private readonly IPeopleRepository _people;
    private readonly ITimeRecordRepository _records;
    private readonly ILibraryRepository _library;
    private readonly IDisplayOutputService _display;
    private readonly ISyncService _sync;
    private CancellationTokenSource? _periodic;
    private readonly Func<DateTime> _now;
    private bool _hasLoaded;

    public HomeViewModel(
        SessionStore session,
        SignedInNavigator navigator,
        IModuleSettingsRepository modules,
        IServiceTypeRepository types,
        IPeopleRepository people,
        ITimeRecordRepository records,
        ILibraryRepository library,
        IDisplayOutputService display,
        ISyncService sync,
        ChurchClock clock,
        Func<DateTime>? now = null)
    {
        _session = session;
        _navigator = navigator;
        _modules = modules;
        _types = types;
        _people = people;
        _records = records;
        _library = library;
        _display = display;
        _sync = sync;
        _now = now ?? (() => clock.Now);
    }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial ChurchModules Modules { get; set; } = ChurchModules.All;

    public IReadOnlyList<ServiceType> ServiceTypes { get; private set; } = [];

    public IReadOnlyList<Person> People { get; private set; } = [];

    public int RecordCount { get; private set; }

    public (int Lyrics, int Music, int Images, int Videos) Library { get; private set; }

    public bool IsDisplayConnected { get; private set; }

    [ObservableProperty]
    public partial Guid? SelectedServiceTypeId { get; set; }

    /// <summary>
    /// Replaced (never mutated) on every rebuild: this view model outlives its page, and a collection that raises change events
    /// would call into the native handlers of pages that were already torn down.
    /// </summary>
    public IReadOnlyList<ServiceTypeOptionViewModel> TypeOptions { get; private set; } = [];

    // ===== Greeting =====

    public string DateOverline => Spanish.DayAndMonth(_now()).ToUpper(Spanish.Culture);

    public string Greeting => _now().Hour switch
    {
        < 12 => "Buenos días,",
        < 19 => "Buenas tardes,",
        _ => "Buenas noches,",
    };

    public string ChurchName => _session.Session?.Church.Name ?? string.Empty;

    public string DisplayStatusText => IsDisplayConnected ? "Todo listo: el TV está conectado." : "Conecta el TV antes de comenzar el servicio.";

    // ===== Hero =====

    public ServiceType? SelectedType => ServiceTypes.FirstOrDefault(t => t.Id == SelectedServiceTypeId);

    public bool NeedsServiceSetup => _hasLoaded && ServiceTypes.Count == 0;

    public bool HasServiceTypes => ServiceTypes.Count > 0;

    public bool IsToday => SelectedType?.Schedule is { } s && s.DayOfWeek == _now().DayOfWeek;

    public bool IsNotToday => !IsToday;

    public string HeroOverline => IsToday ? "SERVICIO DE HOY" : "PRÓXIMO SERVICIO";

    public string HeroTitle => NeedsServiceSetup ? "Crea tu primer servicio" : SelectedType?.Name ?? string.Empty;

    public string HeroColor => SelectedType?.Color ?? "#9B5CFF";

    public string ScheduleText => SelectedType?.Schedule is { } s
        ? $"{(IsToday ? "Hoy" : Spanish.Capitalize(Spanish.Weekday(s.DayOfWeek)))} · {Spanish.Time(s.Hour, s.Minute)}"
        : "Sin horario";

    /// <summary>Blocks only matter when the time-control module is on.</summary>
    public bool ShowsBlockPlan => Modules.TimeControl && SelectedType is { TracksTime: true };

    public bool ShowsLogoPreview => !ShowsBlockPlan;

    public bool ShowsProjectionOnlyNote => !ShowsBlockPlan && HasServiceTypes;

    public string BlocksText => ShowsBlockPlan && SelectedType is { } type
        ? IrisDurationFormat.BlocksSummary(type.Blocks.Count, type.PlannedMinutes)
        : "Solo proyección";

    public string BlocksTotalText => SelectedType is { } type ? IrisDurationFormat.Planned(type.PlannedMinutes) : string.Empty;

    public IReadOnlyList<int> BlockMinutes => SelectedType?.Blocks.Select(b => b.PlannedMinutes).ToList() ?? [];

    public IReadOnlyList<BlockPlanRow> BlockPlan => SelectedType?.Blocks
        .Select((b, i) => new BlockPlanRow(i, b.Name, PersonName(b.DefaultPersonId), $"{b.PlannedMinutes} min"))
        .ToList() ?? [];

    public ProjectionFrame LogoFrame => new(null, new LogoContent(ChurchName));

    // ===== Tiles =====

    public bool ShowsTimeControl => Modules.TimeControl;

    public bool ShowsMultimedia => Modules.Multimedia;

    public string RecordsText => RecordCount switch
    {
        0 => "Aún no hay registros",
        1 => "1 servicio registrado",
        var n => $"{n} servicios registrados",
    };

    public string ServiceTypesText => Plural.Count(ServiceTypes.Count, "tipo de servicio", "tipos de servicio");

    public IReadOnlyList<ServiceTypeSummary> ServiceSummaries => ServiceTypes
        .Select(t => new ServiceTypeSummary(t.Name, t.Color, t.TracksTime && Modules.TimeControl))
        .ToList();

    public string LyricsCount => Library.Lyrics.ToString(Spanish.Culture);

    public string MusicCount => Library.Music.ToString(Spanish.Culture);

    public string ImagesCount => Library.Images.ToString(Spanish.Culture);

    public string VideosCount => Library.Videos.ToString(Spanish.Culture);

    public IReadOnlyList<AvatarItem> PeopleAvatars => People.Take(5).Select((p, i) => new AvatarItem(p.Initials, i)).ToList();

    public bool HasMorePeople => People.Count > 5;

    public string MorePeopleText => $"+{People.Count - 5}";

    public string PeopleText => People.Count == 1 ? "1 persona registrada" : $"{People.Count} personas registradas";

    public IReadOnlyList<ModuleStatus> ModuleStatuses =>
    [
        new("Letras", true),
        new("Biblia", Modules.Bible),
        new("Multimedia", Modules.Multimedia),
        new("Control de tiempo", Modules.TimeControl),
    ];

    // ===== Sync triggers (api-contract §12): back on Home and every 5 min while Home is visible =====

    public void Activate()
    {
        _display.DisplayChanged -= OnDisplayChanged;
        _display.DisplayChanged += OnDisplayChanged;
        if (_hasLoaded)
        {
            _ = _sync.SyncNowAsync(SyncReason.Home);
            _ = _session.RefreshSessionAsync();
        }

        _periodic?.Cancel();
        var loop = _periodic = new CancellationTokenSource();
        _ = RunPeriodicSyncAsync(loop.Token);
    }

    public void Deactivate()
    {
        _display.DisplayChanged -= OnDisplayChanged;
        _periodic?.Cancel();
        _periodic = null;
    }

    // The TV can be plugged in or pulled out while Home is open.
    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        IsDisplayConnected = _display.ConnectedDisplay() is not null;
        OnPropertyChanged(nameof(IsDisplayConnected));
        OnPropertyChanged(nameof(DisplayStatusText));
    }

    private async Task RunPeriodicSyncAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), token);
                await _sync.SyncNowAsync(SyncReason.Timer);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ===== Intents =====

    /// <summary>First time: load with a spinner. Afterwards: silent refresh.</summary>
    [RelayCommand]
    private async Task AppearAsync()
    {
        if (!_hasLoaded)
        {
            IsLoading = true;
        }

        try
        {
            // Modules first: they decide what the rest shows.
            Modules = await _modules.ModulesAsync();
            var types = _types.ServiceTypesAsync();
            var people = _people.PeopleAsync();
            var records = _records.RecordsAsync();
            var lyrics = _library.LyricsAsync();
            var music = _library.MediaAsync(MediaKind.Music);
            var images = _library.MediaAsync(MediaKind.Image);
            var videos = _library.MediaAsync(MediaKind.Video);

            ServiceTypes = await types;
            People = (await people).OrderBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true)).ToList();
            RecordCount = (await records).Count;
            Library = ((await lyrics).Count, (await music).Count, (await images).Count, (await videos).Count);
            IsDisplayConnected = _display.ConnectedDisplay() is not null;

            if (SelectedServiceTypeId is not { } id || ServiceTypes.All(t => t.Id != id))
            {
                SelectedServiceTypeId = SuggestedType()?.Id;
            }

            _hasLoaded = true;
            RebuildOptions();
            OnPropertyChanged(string.Empty);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectServiceType(ServiceTypeOptionViewModel option)
    {
        SelectedServiceTypeId = option.Type.Id;
        foreach (var o in TypeOptions)
        {
            o.IsSelected = o.Type.Id == option.Type.Id;
        }

        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void StartSelectedService()
    {
        if (SelectedType is { } type)
        {
            _navigator.StartService(new ConsoleLaunch(type, Modules, People, _now()));
        }
    }

    [RelayCommand]
    private void OpenServices() => _navigator.Open(AppRoute.Services);

    [RelayCommand]
    private void OpenModules() => _navigator.Open(AppRoute.Modules);

    [RelayCommand]
    private void OpenTimes() => _navigator.Open(AppRoute.Times);

    [RelayCommand]
    private void OpenPeople() => _navigator.Open(AppRoute.People);

    /// <summary>Today's scheduled service closest ahead (up to 2 h after it started); otherwise the first one.</summary>
    public ServiceType? SuggestedType()
    {
        var now = _now();
        return ServiceTypes
            .Where(t => t.Schedule is { } s && s.DayOfWeek == now.DayOfWeek)
            .Select(t => (Type: t, Start: now.Date.AddHours(t.Schedule!.Hour).AddMinutes(t.Schedule.Minute)))
            .Where(x => now <= x.Start.AddHours(2))
            .OrderBy(x => x.Start)
            .Select(x => x.Type)
            .FirstOrDefault() ?? ServiceTypes.FirstOrDefault();
    }

    private void RebuildOptions()
    {
        TypeOptions = ServiceTypes.Select(type => new ServiceTypeOptionViewModel(type, this) { IsSelected = type.Id == SelectedServiceTypeId }).ToList();
        OnPropertyChanged(nameof(TypeOptions));
    }

    private string PersonName(Guid? id) => id is { } value ? People.FirstOrDefault(p => p.Id == value)?.Name ?? string.Empty : string.Empty;
}
