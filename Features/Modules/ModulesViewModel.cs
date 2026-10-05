using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Shell;

namespace Iris.Features.Modules;

public enum ModuleKind
{
    Lyrics,
    Bible,
    Multimedia,
    TimeControl,
}

/// <summary>A module row: tinted icon, title, description and a switch that saves immediately.</summary>
public sealed partial class ModuleRowViewModel(ModuleKind kind, string title, string description, string glyph, string tintKey, ModulesViewModel owner) : ObservableObject
{
    private bool _isSyncing;

    public ModuleKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public string Glyph { get; } = glyph;

    public string TintKey { get; } = tintKey;

    /// <summary>Lyrics are always on.</summary>
    public bool CanToggle => Kind != ModuleKind.Lyrics && owner.CanManage;

    public bool IsTimeControl => Kind == ModuleKind.TimeControl;

    /// <summary>Divider above every row but the first.</summary>
    public bool HasDivider => Kind != ModuleKind.Lyrics;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Sets the value coming from the view model without saving again.</summary>
    public void Sync(bool isOn)
    {
        _isSyncing = true;
        IsOn = isOn;
        _isSyncing = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_isSyncing)
        {
            _ = owner.SetModuleAsync(Kind, value);
        }
    }
}

/// <summary>
/// Módulos (IRIS_SPEC §6.6): immediate save on every switch, saves chained in order; a failed save
/// flips its switch back and shows an error banner.
/// </summary>
public sealed partial class ModulesViewModel : ObservableObject
{
    private readonly IModuleSettingsRepository _repository;
    private readonly SignedInNavigator _navigator;
    private readonly SessionStore _session;

    public ModulesViewModel(IModuleSettingsRepository repository, SignedInNavigator navigator, SessionStore session)
    {
        _session = session;
        _repository = repository;
        _navigator = navigator;
        Rows =
        [
            new(ModuleKind.Lyrics, "Letras", "Proyecta letras de canciones y anuncios. Siempre activo.", "", "IrisEmberColor", this),
            new(ModuleKind.Bible, "Biblia", "Busca y proyecta versículos por libro, capítulo y versículo.", "", "IrisVioletColor", this),
            new(ModuleKind.Multimedia, "Multimedia", "Música, imágenes y videos en la biblioteca y el reproductor.", "", "IrisRoseColor", this),
            new(ModuleKind.TimeControl, "Control de tiempo", "Mide los bloques de cada servicio y guarda sus tiempos.", "", "IrisCoralColor", this),
        ];
        Sync();
    }

    public IReadOnlyList<ModuleRowViewModel> Rows { get; }

    /// <summary>Only <see cref="Permission.ModulesManage"/> may switch modules (api-contract §3).</summary>
    public bool CanManage => _session.Can(Permission.ModulesManage);

    public string? ReadOnlyNote => CanManage ? null : "Solo un administrador puede cambiar los módulos.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimeControlOff))]
    public partial ChurchModules Modules { get; set; } = ChurchModules.All;

    public bool IsTimeControlOff => !Modules.TimeControl;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>The tail of the save chain (tests await it).</summary>
    public Task SaveTask { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Modules = await _repository.ModulesAsync();
        Sync();
        IsLoading = false;
    }

    [RelayCommand]
    private void GoHome() => _navigator.GoHome();

    public Task SetModuleAsync(ModuleKind kind, bool isOn)
    {
        if (kind == ModuleKind.Lyrics || Value(Modules, kind) == isOn)
        {
            return SaveTask;
        }

        ErrorMessage = null;
        Modules = With(Modules, kind, isOn);
        Sync();
        var snapshot = Modules;
        return SaveTask = SaveAfter(SaveTask, snapshot, kind, !isOn);
    }

    private async Task SaveAfter(Task previous, ChurchModules snapshot, ModuleKind kind, bool previousValue)
    {
        await previous;
        try
        {
            await _repository.SaveAsync(snapshot);
        }
        catch
        {
            Modules = With(Modules, kind, previousValue);
            Sync();
            ErrorMessage = "Algo salió mal. Inténtalo de nuevo.";
        }
    }

    private void Sync()
    {
        foreach (var row in Rows)
        {
            row.Sync(Value(Modules, row.Kind));
        }
    }

    private static bool Value(ChurchModules modules, ModuleKind kind) => kind switch
    {
        ModuleKind.Bible => modules.Bible,
        ModuleKind.Multimedia => modules.Multimedia,
        ModuleKind.TimeControl => modules.TimeControl,
        _ => true,
    };

    private static ChurchModules With(ChurchModules modules, ModuleKind kind, bool isOn) => kind switch
    {
        ModuleKind.Bible => modules with { Bible = isOn },
        ModuleKind.Multimedia => modules with { Multimedia = isOn },
        ModuleKind.TimeControl => modules with { TimeControl = isOn },
        _ => modules,
    };
}
