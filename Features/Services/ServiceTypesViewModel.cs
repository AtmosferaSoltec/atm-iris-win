using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Shell;

namespace Iris.Features.Services;

/// <summary>A service-type card: color stripe, name, schedule and its blocks (or "Solo proyección").</summary>
public sealed class ServiceTypeCardViewModel(ServiceType type, bool showsBlocks, ServiceTypesViewModel owner)
{
    public ServiceType Type { get; } = type;

    public ServiceTypesViewModel Owner { get; } = owner;

    public string Name => Type.Name;

    public string Color => Type.Color;

    public string ScheduleText => ScheduleFormat.Text(Type.Schedule);

    /// <summary>Blocks only show with the time-control module on.</summary>
    public bool ShowsBlocks { get; } = showsBlocks && type.TracksTime;

    public bool IsProjectionOnly => !ShowsBlocks;

    public IReadOnlyList<int> Minutes => Type.Blocks.Select(b => b.PlannedMinutes).ToList();

    public string BlocksText => IrisDurationFormat.BlocksSummary(Type.Blocks.Count, Type.PlannedMinutes);

    public string AccessibleName => $"{Name}. {ScheduleText}. {(ShowsBlocks ? BlocksText : "Solo proyección")}";
}

public static class ScheduleFormat
{
    /// <summary>"Domingo · 10:00" or "Sin horario".</summary>
    public static string Text(ServiceSchedule? schedule) => schedule is { } s
        ? $"{Spanish.Capitalize(Spanish.Weekday(s.DayOfWeek))} · {Spanish.Time(s.Hour, s.Minute)}"
        : "Sin horario";
}

/// <summary>What the editor returns: a saved type or a deleted id (null = cancelled).</summary>
public sealed record EditorResult(ServiceType? Saved, Guid? DeletedId);

/// <summary>
/// Servicios (IRIS_SPEC §6.8): service-type cards; clicking one opens the editor, whose result
/// updates the list without reloading.
/// </summary>
public sealed partial class ServiceTypesViewModel : ObservableObject
{
    private readonly IServiceTypeRepository _types;
    private readonly IModuleSettingsRepository _modules;
    private readonly SignedInNavigator _navigator;
    private readonly SessionStore _session;
    private List<ServiceType> _all = [];

    public ServiceTypesViewModel(IServiceTypeRepository types, IModuleSettingsRepository modules, SignedInNavigator navigator, SessionStore session)
    {
        _session = session;
        _types = types;
        _modules = modules;
        _navigator = navigator;
    }

    public ObservableCollection<ServiceTypeCardViewModel> Cards { get; } = [];

    /// <summary>Without <see cref="Permission.ServiceTypesManage"/> there is no "Nuevo servicio" and the editor is read-only.</summary>
    public bool CanManage => _session.Can(Permission.ServiceTypesManage);

    public IReadOnlyList<ServiceType> ServiceTypes => _all;

    [ObservableProperty]
    public partial ChurchModules Modules { get; set; } = ChurchModules.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && Cards.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    public partial ServiceTypeEditorViewModel? Editor { get; set; }

    public bool IsEditorOpen => Editor is not null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = Cards.Count == 0;
        Modules = await _modules.ModulesAsync();
        _all = (await _types.ServiceTypesAsync()).ToList();
        Rebuild();
        IsLoading = false;
    }

    [RelayCommand]
    private void GoHome() => _navigator.GoHome();

    [RelayCommand]
    private void CreateServiceType()
    {
        if (CanManage)
        {
            OpenEditor(null);
        }
    }

    [RelayCommand]
    private void Edit(ServiceTypeCardViewModel card) => OpenEditor(card.Type);

    private void OpenEditor(ServiceType? type) =>
        Editor = new ServiceTypeEditorViewModel(type, _all, Modules, _types, OnEditorDone, CanManage);

    private void OnEditorDone(EditorResult? result)
    {
        Editor = null;
        if (result?.Saved is { } saved)
        {
            var index = _all.FindIndex(t => t.Id == saved.Id);
            if (index >= 0)
            {
                _all[index] = saved;
            }
            else
            {
                _all.Add(saved);
            }
        }
        else if (result?.DeletedId is { } id)
        {
            _all.RemoveAll(t => t.Id == id);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        Cards.Clear();
        foreach (var type in _all)
        {
            Cards.Add(new ServiceTypeCardViewModel(type, Modules.TimeControl, this));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
