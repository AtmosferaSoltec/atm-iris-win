using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Formatting;
using Iris.Core.Models;
using Iris.Core.Timing;
using Iris.Features.Services;

namespace Iris.Features.LiveConsole;

/// <summary>A person in the leader picker (suggested one first, with a "Sugerido" chip).</summary>
public sealed partial class PickerPersonViewModel(Person person, int colorIndex, bool isSuggested, ResponsiblePickerViewModel owner) : ObservableObject
{
    public Person Person { get; } = person;

    public string Name => Person.Name;

    public string Initials => Person.Initials;

    public int ColorIndex { get; } = colorIndex;

    public bool IsSuggested { get; } = isSuggested;

    public ResponsiblePickerViewModel Owner { get; } = owner;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// "¿Quién dirige {bloque}?" (IRIS_SPEC §6.9): pick the leader right before the block starts.
/// Adding a name that already exists selects that person.
/// </summary>
public sealed partial class ResponsiblePickerViewModel : ObservableObject
{
    private readonly Func<string, Task<Person>> _addPerson;
    private readonly Action<Guid?> _onConfirm;
    private List<Person> _people;

    public ResponsiblePickerViewModel(string blockName, Guid? suggestedPersonId, IReadOnlyList<Person> people, Func<string, Task<Person>> addPerson, Action<Guid?> onConfirm)
    {
        BlockName = blockName;
        SuggestedPersonId = suggestedPersonId;
        SelectedPersonId = suggestedPersonId is { } id && people.Any(p => p.Id == id) ? id : null;
        _people = people.ToList();
        _addPerson = addPerson;
        _onConfirm = onConfirm;
        Rebuild();
    }

    public string BlockName { get; }

    public Guid? SuggestedPersonId { get; }

    public string Title => $"¿Quién dirige {BlockName}?";

    public string ConfirmText => $"Comenzar {BlockName}";

    public ObservableCollection<PickerPersonViewModel> People { get; } = [];

    [ObservableProperty]
    public partial Guid? SelectedPersonId { get; set; }

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewNameError { get; set; }

    partial void OnNewNameChanged(string value) => NewNameError = null;

    [RelayCommand]
    private void Select(PickerPersonViewModel person)
    {
        SelectedPersonId = person.Person.Id;
        foreach (var p in People)
        {
            p.IsSelected = p.Person.Id == SelectedPersonId;
        }
    }

    [RelayCommand]
    private void Confirm() => _onConfirm(SelectedPersonId);

    [RelayCommand]
    private void ConfirmWithoutLeader() => _onConfirm(null);

    [RelayCommand]
    private async Task AddPersonAsync()
    {
        var key = NameKey.For(NewName);
        if (key.Length == 0)
        {
            NewNameError = "Escribe un nombre.";
            return;
        }

        var person = _people.FirstOrDefault(p => NameKey.For(p.Name) == key);
        if (person is null)
        {
            try
            {
                person = await _addPerson(NewName.Trim());
            }
            catch
            {
                NewNameError = "Algo salió mal. Inténtalo de nuevo.";
                return;
            }

            _people.Add(person);
        }

        NewName = string.Empty;
        SelectedPersonId = person.Id;
        Rebuild();
    }

    private void Rebuild()
    {
        People.Clear();
        var ordered = _people
            .OrderByDescending(p => p.Id == SuggestedPersonId)
            .ThenBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true));
        var index = 0;
        foreach (var person in ordered)
        {
            People.Add(new PickerPersonViewModel(person, index++, person.Id == SuggestedPersonId, this) { IsSelected = person.Id == SelectedPersonId });
        }
    }
}

/// <summary>"Agregar bloque" during the service: inserted after the current block.</summary>
public sealed partial class AddBlockViewModel : ObservableObject
{
    private readonly Action<string, int, Guid?> _onAdd;

    public AddBlockViewModel(IReadOnlyList<Person> people, Action<string, int, Guid?> onAdd)
    {
        _onAdd = onAdd;
        LeaderOptions = [LeaderOption.Nobody, .. people.OrderBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true)).Select(p => new LeaderOption(p.Id, p.Name))];
        Leader = LeaderOption.Nobody;
    }

    public IReadOnlyList<LeaderOption> LeaderOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Minutes { get; set; } = 10;

    [ObservableProperty]
    public partial LeaderOption? Leader { get; set; }

    public bool CanAdd => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand]
    private void Add()
    {
        if (CanAdd)
        {
            var minutes = double.IsNaN(Minutes) ? 10 : (int)Math.Clamp(Math.Round(Minutes), 1, 240);
            _onAdd(Name.Trim(), minutes, Leader?.PersonId);
        }
    }
}

/// <summary>A row of "Bloques pendientes": rename, minutes, leader, skip / restore.</summary>
public sealed partial class PendingBlockViewModel : ObservableObject
{
    private readonly BlockTimerViewModel _owner;
    private bool _isSyncing;

    public PendingBlockViewModel(TimerBlock block, IReadOnlyList<LeaderOption> leaders, BlockTimerViewModel owner)
    {
        _owner = owner;
        Id = block.Id;
        LeaderOptions = leaders;
        Sync(block);
    }

    public Guid Id { get; }

    public BlockTimerViewModel Owner => _owner;

    public IReadOnlyList<LeaderOption> LeaderOptions { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Minutes { get; set; }

    [ObservableProperty]
    public partial LeaderOption? Leader { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipText), nameof(IsActive))]
    public partial bool IsSkipped { get; set; }

    public bool IsActive => !IsSkipped;

    public string SkipText => IsSkipped ? "Restaurar" : "Omitir";

    public void Sync(TimerBlock block)
    {
        _isSyncing = true;
        Name = block.Name;
        Minutes = block.PlannedSeconds / 60;
        Leader = LeaderOptions.FirstOrDefault(o => o.PersonId == block.PersonId) ?? LeaderOption.Nobody;
        IsSkipped = block.IsSkipped;
        _isSyncing = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!_isSyncing)
        {
            _owner.RenamePendingBlock(Id, value);
        }
    }

    partial void OnMinutesChanged(double value)
    {
        if (!_isSyncing && !double.IsNaN(value))
        {
            _owner.SetPendingMinutes(Id, (int)Math.Clamp(Math.Round(value), 1, 240));
        }
    }

    partial void OnLeaderChanged(LeaderOption? value)
    {
        if (!_isSyncing && value is not null)
        {
            _owner.SetPendingLeader(Id, value.PersonId);
        }
    }
}

/// <summary>A breadcrumb under the running block: done ✓ time, current ●, pending, skipped (struck through).</summary>
public sealed record BlockCrumb(string Name, string Detail, bool IsDone, bool IsCurrent, bool IsSkipped, bool IsOver, bool IsLast)
{
    public bool HasDetail => Detail.Length > 0;

    public bool IsPending => !IsDone && !IsCurrent && !IsSkipped;
}

/// <summary>A choice in the current block's leader menu.</summary>
public sealed record LeaderMenuItem(Guid? PersonId, string Name, bool IsSelected, BlockTimerViewModel Owner);
