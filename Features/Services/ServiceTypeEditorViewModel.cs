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

namespace Iris.Features.Services;

/// <summary>An entry of a leader menu: nobody, a person, or "Agregar persona…".</summary>
public sealed record LeaderOption(Guid? PersonId, string Name, bool IsAddAction = false)
{
    public static readonly LeaderOption Nobody = new(null, "Sin responsable");
    public static readonly LeaderOption AddPerson = new(null, "Agregar persona…", IsAddAction: true);

    public override string ToString() => Name;
}

public sealed partial class ColorOptionViewModel(string hex, string name, ServiceTypeEditorViewModel owner) : ObservableObject
{
    public string Hex { get; } = hex;

    public string Name { get; } = name;

    public ServiceTypeEditorViewModel Owner { get; } = owner;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

public sealed partial class WeekdayOptionViewModel(int weekday, ServiceTypeEditorViewModel owner) : ObservableObject
{
    /// <summary>1 = Sunday … 7 = Saturday.</summary>
    public int Weekday { get; } = weekday;

    public string Name { get; } = Spanish.WeekdayPill((DayOfWeek)(weekday - 1));

    public string FullName { get; } = Spanish.Capitalize(Spanish.Weekday((DayOfWeek)(weekday - 1)));

    public ServiceTypeEditorViewModel Owner { get; } = owner;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>An editable block row: name, minutes (1…240) and suggested leader.</summary>
public sealed partial class BlockDraftViewModel : ObservableObject
{
    private readonly ServiceTypeEditorViewModel _owner;

    public BlockDraftViewModel(Guid id, string name, int minutes, Guid? personId, ServiceTypeEditorViewModel owner)
    {
        Id = id;
        _owner = owner;
        Name = name;
        Minutes = minutes;
        Leader = owner.LeaderFor(personId);
    }

    public Guid Id { get; }

    public ServiceTypeEditorViewModel Owner => _owner;

    public IReadOnlyList<LeaderOption> LeaderOptions => _owner.LeaderOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    public partial string Name { get; set; }

    public bool HasNameError => string.IsNullOrWhiteSpace(Name);

    [ObservableProperty]
    public partial double Minutes { get; set; }

    [ObservableProperty]
    public partial LeaderOption? Leader { get; set; }

    public int WholeMinutes => double.IsNaN(Minutes) ? 1 : (int)Math.Clamp(Math.Round(Minutes), 1, 240);

    partial void OnNameChanged(string value) => _owner.BlocksChanged();

    partial void OnMinutesChanged(double value)
    {
        if (double.IsNaN(value) || value < 1 || value > 240 || value != Math.Round(value))
        {
            Minutes = WholeMinutes;
            return;
        }

        _owner.BlocksChanged();
    }

    partial void OnLeaderChanged(LeaderOption? oldValue, LeaderOption? newValue)
    {
        if (newValue is { IsAddAction: true })
        {
            // "Agregar persona…" is an action, not a choice: keep the previous leader and ask for a name.
            Leader = oldValue;
            _owner.BeginAddPerson(this);
        }
    }

    public void RefreshLeaderOptions()
    {
        var id = Leader?.PersonId;
        OnPropertyChanged(nameof(LeaderOptions));
        Leader = _owner.LeaderFor(id);
    }
}

/// <summary>
/// Service-type editor (IRIS_SPEC §6.8). Works on a copy; only "Guardar" persists. With the
/// time-control module off the blocks section is hidden and existing blocks are saved untouched.
/// </summary>
public sealed partial class ServiceTypeEditorViewModel : ObservableObject
{
    private readonly ServiceType? _original;
    private readonly IReadOnlyList<ServiceType> _allTypes;
    private readonly IServiceTypeRepository _types;
    private readonly IPeopleRepository _peopleRepository;
    private readonly Action<EditorResult?> _onDone;
    private List<Person> _people;
    private BlockDraftViewModel? _addingPersonFor;

    public ServiceTypeEditorViewModel(
        ServiceType? original,
        IReadOnlyList<ServiceType> allTypes,
        ChurchModules modules,
        IReadOnlyList<Person> people,
        IServiceTypeRepository types,
        IPeopleRepository peopleRepository,
        Action<EditorResult?> onDone,
        bool canEdit = true)
    {
        CanEdit = canEdit;
        _original = original;
        _allTypes = allTypes;
        _types = types;
        _peopleRepository = peopleRepository;
        _onDone = onDone;
        _people = people.OrderBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true)).ToList();
        Modules = modules;
        LeaderOptions = BuildLeaderOptions();

        ColorOptions = ServicePalette.Colors.Select(c => new ColorOptionViewModel(c.Hex, c.Name, this)).ToList();
        WeekdayOptions = Enumerable.Range(1, 7).Select(d => new WeekdayOptionViewModel(d, this)).ToList();

        Name = original?.Name ?? string.Empty;
        SelectColorCore(original?.Color ?? ServicePalette.Colors[0].Hex);
        HasSchedule = original?.Schedule is not null;
        SelectWeekdayCore(original?.Schedule?.Weekday ?? 1);
        Time = original?.Schedule is { } s ? new TimeSpan(s.Hour, s.Minute, 0) : new TimeSpan(10, 0, 0);
        foreach (var block in original?.Blocks ?? [])
        {
            Blocks.Add(new BlockDraftViewModel(block.Id, block.Name, block.PlannedMinutes, block.DefaultPersonId, this));
        }

        TracksTime = Blocks.Count > 0;
        Blocks.CollectionChanged += (_, _) => BlocksChanged();
        Validate();
    }

    /// <summary>False without <see cref="Permission.ServiceTypesManage"/>: the sheet opens read-only.</summary>
    public bool CanEdit { get; }

    public string CancelText => CanEdit ? "Cancelar" : "Cerrar";

    public bool ShowsDelete => IsEditing && CanEdit;

    public bool IsNew => _original is null;

    public bool IsEditing => !IsNew;

    public string Title => !CanEdit ? "Servicio" : IsNew ? "Nuevo servicio" : "Editar servicio";

    public ChurchModules Modules { get; }

    /// <summary>The CONTROL DE TIEMPO section only exists with the module on.</summary>
    public bool ShowsTimeControl => Modules.TimeControl;

    public IReadOnlyList<ColorOptionViewModel> ColorOptions { get; }

    public IReadOnlyList<WeekdayOptionViewModel> WeekdayOptions { get; }

    public IReadOnlyList<LeaderOption> LeaderOptions { get; private set; }

    // ===== Draft =====

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string Color { get; set; } = ServicePalette.Colors[0].Hex;

    [ObservableProperty]
    public partial bool HasSchedule { get; set; }

    [ObservableProperty]
    public partial int Weekday { get; set; } = 1;

    [ObservableProperty]
    public partial TimeSpan Time { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBlocks))]
    public partial bool TracksTime { get; set; }

    public bool ShowsBlocks => TracksTime && ShowsTimeControl;

    public ObservableCollection<BlockDraftViewModel> Blocks { get; } = [];

    [ObservableProperty]
    public partial string? BlocksError { get; set; }

    public IReadOnlyList<int> BlockMinutes => Blocks.Select(b => b.WholeMinutes).ToList();

    public string TotalText => $"Total previsto: {IrisDurationFormat.Planned(Blocks.Sum(b => b.WholeMinutes))}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsSaving { get; set; }

    public bool CanSave => CanEdit && !IsSaving && !string.IsNullOrWhiteSpace(Name) && NameError is null && BlocksError is null
        && (!ShowsBlocks || Blocks.All(b => !b.HasNameError));

    // ===== Confirmations =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    public partial bool IsConfirmingBlockRemoval { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    public partial bool IsConfirmingDelete { get; set; }

    public string DeleteTitle => $"¿Eliminar {_original?.Name ?? Name}?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    public partial bool IsAddingPerson { get; set; }

    [ObservableProperty]
    public partial string NewPersonName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewPersonError { get; set; }

    public bool IsModalOpen => IsConfirmingBlockRemoval || IsConfirmingDelete || IsAddingPerson;

    // ===== Draft changes =====

    partial void OnNameChanged(string value) => Validate();

    [RelayCommand]
    private void SelectColor(ColorOptionViewModel option) => SelectColorCore(option.Hex);

    private void SelectColorCore(string hex)
    {
        Color = hex;
        foreach (var option in ColorOptions)
        {
            option.IsSelected = string.Equals(option.Hex, hex, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void SelectWeekday(WeekdayOptionViewModel option) => SelectWeekdayCore(option.Weekday);

    private void SelectWeekdayCore(int weekday)
    {
        Weekday = weekday;
        foreach (var option in WeekdayOptions)
        {
            option.IsSelected = option.Weekday == weekday;
        }
    }

    /// <summary>Turning on adds a first "Nuevo bloque" (10 min); turning off with blocks asks first.</summary>
    public void SetTracksTime(bool isOn)
    {
        if (isOn == TracksTime)
        {
            return;
        }

        if (isOn)
        {
            TracksTime = true;
            if (Blocks.Count == 0)
            {
                AddBlock();
            }
        }
        else if (Blocks.Count > 0)
        {
            IsConfirmingBlockRemoval = true;
        }
        else
        {
            TracksTime = false;
        }

        Validate();
    }

    [RelayCommand]
    private void ConfirmBlockRemoval()
    {
        IsConfirmingBlockRemoval = false;
        Blocks.Clear();
        TracksTime = false;
        Validate();
    }

    [RelayCommand]
    private void CancelBlockRemoval() => IsConfirmingBlockRemoval = false;

    [RelayCommand]
    private void AddBlock() => Blocks.Add(new BlockDraftViewModel(Guid.NewGuid(), "Nuevo bloque", 10, null, this));

    [RelayCommand]
    private void RemoveBlock(BlockDraftViewModel block) => Blocks.Remove(block);

    /// <summary>Moves a block to another position (drag and drop also reorders the collection directly).</summary>
    public void MoveBlock(int from, int to)
    {
        if (from >= 0 && from < Blocks.Count && to >= 0 && to < Blocks.Count && from != to)
        {
            Blocks.Move(from, to);
        }
    }

    public void BlocksChanged()
    {
        OnPropertyChanged(nameof(BlockMinutes));
        OnPropertyChanged(nameof(TotalText));
        Validate();
    }

    // ===== Add person (from a leader menu) =====

    public void BeginAddPerson(BlockDraftViewModel block)
    {
        _addingPersonFor = block;
        NewPersonName = string.Empty;
        NewPersonError = null;
        IsAddingPerson = true;
    }

    partial void OnNewPersonNameChanged(string value) => NewPersonError = null;

    [RelayCommand]
    private void CancelAddPerson()
    {
        IsAddingPerson = false;
        _addingPersonFor = null;
    }

    /// <summary>If the name already exists, that person is used.</summary>
    [RelayCommand]
    private async Task ConfirmAddPersonAsync()
    {
        var key = NameKey.For(NewPersonName);
        if (key.Length == 0)
        {
            NewPersonError = "Escribe un nombre.";
            return;
        }

        var person = _people.FirstOrDefault(p => NameKey.For(p.Name) == key);
        if (person is null)
        {
            try
            {
                person = await _peopleRepository.AddAsync(NewPersonName.Trim());
            }
            catch
            {
                NewPersonError = "Algo salió mal. Inténtalo de nuevo.";
                return;
            }

            _people = [.. _people, person];
            _people.Sort((a, b) => string.Compare(a.Name, b.Name, Spanish.Culture, System.Globalization.CompareOptions.IgnoreCase));
            LeaderOptions = BuildLeaderOptions();
            OnPropertyChanged(nameof(LeaderOptions));
            foreach (var block in Blocks)
            {
                block.RefreshLeaderOptions();
            }
        }

        if (_addingPersonFor is { } target)
        {
            target.Leader = LeaderFor(person.Id);
        }

        IsAddingPerson = false;
        _addingPersonFor = null;
    }

    public LeaderOption LeaderFor(Guid? personId) =>
        personId is { } id ? LeaderOptions.FirstOrDefault(o => o.PersonId == id) ?? LeaderOption.Nobody : LeaderOption.Nobody;

    private IReadOnlyList<LeaderOption> BuildLeaderOptions() =>
        [LeaderOption.Nobody, .. _people.Select(p => new LeaderOption(p.Id, p.Name)), LeaderOption.AddPerson];

    // ===== Save / delete =====

    [RelayCommand]
    private void Cancel() => _onDone(null);

    [RelayCommand]
    private async Task SaveAsync()
    {
        Validate();
        if (!CanSave)
        {
            return;
        }

        var type = BuildType();
        IsSaving = true;
        try
        {
            await _types.SaveAsync(type);
            _onDone(new EditorResult(type, null));
        }
        catch
        {
            NameError = "Algo salió mal. Inténtalo de nuevo.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    public ServiceType BuildType()
    {
        var blocks = ShowsTimeControl
            ? (TracksTime ? Blocks.Select(b => new BlockTemplate(b.Id, b.Name.Trim(), b.WholeMinutes, b.Leader?.PersonId)).ToList() : [])
            : _original?.Blocks ?? [];
        var schedule = HasSchedule ? new ServiceSchedule(Weekday, Time.Hours, Time.Minutes) : null;
        return new ServiceType(_original?.Id ?? Guid.NewGuid(), Name.Trim(), Color, schedule, blocks);
    }

    [RelayCommand]
    private void RequestDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        IsConfirmingDelete = false;
        if (_original is null)
        {
            return;
        }

        try
        {
            await _types.DeleteAsync(_original.Id);
            _onDone(new EditorResult(null, _original.Id));
        }
        catch
        {
            NameError = "Algo salió mal. Inténtalo de nuevo.";
        }
    }

    private void Validate()
    {
        var key = NameKey.For(Name);
        NameError = key.Length > 0 && _allTypes.Any(t => t.Id != _original?.Id && NameKey.For(t.Name) == key)
            ? "Ya existe un servicio con ese nombre."
            : null;
        BlocksError = ShowsTimeControl && TracksTime && Blocks.Count == 0
            ? "Agrega al menos un bloque o desactiva el control de tiempo."
            : null;
        OnPropertyChanged(nameof(CanSave));
    }
}
