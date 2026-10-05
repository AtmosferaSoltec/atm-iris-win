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

namespace Iris.Features.People;

public sealed class PersonRowViewModel(Person person, int colorIndex, int blockCount, PeopleViewModel owner)
{
    public Person Person { get; } = person;

    public PeopleViewModel Owner { get; } = owner;

    public string Name => Person.Name;

    public string Initials => Person.Initials;

    public int ColorIndex { get; } = colorIndex;

    /// <summary>Times this person led a block in the records (skipped blocks don't count).</summary>
    public string BlocksText { get; } = Plural.Count(blockCount, "bloque", "bloques");

    public bool HasDivider => ColorIndex > 0;
}

/// <summary>
/// Personas (IRIS_SPEC §6.7): add, rename and delete leaders. Names compare without accents, case
/// or surrounding spaces. Deleting someone keeps their saved times.
/// </summary>
public sealed partial class PeopleViewModel : ObservableObject
{
    private readonly IPeopleRepository _people;
    private readonly ITimeRecordRepository _records;
    private readonly SignedInNavigator _navigator;
    private IReadOnlyList<Person> _all = [];
    private IReadOnlyDictionary<Guid, int> _blockCounts = new Dictionary<Guid, int>();

    public PeopleViewModel(IPeopleRepository people, ITimeRecordRepository records, SignedInNavigator navigator)
    {
        _people = people;
        _records = records;
        _navigator = navigator;
    }

    public ObservableCollection<PersonRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && Rows.Count == 0;

    [ObservableProperty]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewNameError { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // ===== Rename =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenaming), nameof(IsModalOpen))]
    public partial Person? RenameTarget { get; set; }

    public bool IsRenaming => RenameTarget is not null;

    [ObservableProperty]
    public partial string RenameDraft { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? RenameError { get; set; }

    // ===== Delete =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingDelete), nameof(DeleteTitle), nameof(IsModalOpen))]
    public partial Person? PendingDeletion { get; set; }

    public bool IsConfirmingDelete => PendingDeletion is not null;

    public string DeleteTitle => PendingDeletion is null ? string.Empty : $"¿Eliminar a {PendingDeletion.Name}?";

    public bool IsModalOpen => IsRenaming || IsConfirmingDelete;

    /// <summary>Raised after adding so the view keeps focus in the field.</summary>
    public event EventHandler? NameFieldFocusRequested;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = Rows.Count == 0;
        var people = _people.PeopleAsync();
        var records = _records.RecordsAsync();
        _all = await people;
        _blockCounts = (await records)
            .SelectMany(r => r.Blocks)
            .Where(b => b.IsCounted && b.PersonId is not null)
            .GroupBy(b => b.PersonId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        Rebuild();
        IsLoading = false;
    }

    [RelayCommand]
    private void GoHome() => _navigator.GoHome();

    [RelayCommand]
    private async Task AddAsync()
    {
        var error = Validate(NewName, except: null);
        if (error is not null)
        {
            NewNameError = error;
            NameFieldFocusRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            var person = await _people.AddAsync(NewName.Trim());
            _all = [.. _all, person];
            NewName = string.Empty;
            Rebuild();
        }
        catch
        {
            ErrorMessage = "Algo salió mal. Inténtalo de nuevo.";
        }

        NameFieldFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNewNameChanged(string value)
    {
        NewNameError = null;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void BeginRename(PersonRowViewModel row)
    {
        RenameError = null;
        RenameDraft = row.Name;
        RenameTarget = row.Person;
    }

    partial void OnRenameDraftChanged(string value) => RenameError = null;

    [RelayCommand]
    private void CancelRename() => RenameTarget = null;

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        if (RenameTarget is not { } target)
        {
            return;
        }

        if (Validate(RenameDraft, except: target.Id) is { } error)
        {
            RenameError = error;
            return;
        }

        try
        {
            await _people.RenameAsync(target.Id, RenameDraft.Trim());
            _all = _all.Select(p => p.Id == target.Id ? p with { Name = RenameDraft.Trim() } : p).ToList();
            RenameTarget = null;
            Rebuild();
        }
        catch
        {
            RenameError = "Algo salió mal. Inténtalo de nuevo.";
        }
    }

    [RelayCommand]
    private void RequestDelete(PersonRowViewModel row) => PendingDeletion = row.Person;

    [RelayCommand]
    private void CancelDelete() => PendingDeletion = null;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (PendingDeletion is not { } person)
        {
            return;
        }

        PendingDeletion = null;
        try
        {
            await _people.DeleteAsync(person.Id);
            _all = _all.Where(p => p.Id != person.Id).ToList();
            Rebuild();
        }
        catch
        {
            ErrorMessage = "Algo salió mal. Inténtalo de nuevo.";
        }
    }

    /// <summary>"Escribe un nombre." / "Ya existe una persona con ese nombre." or null.</summary>
    public string? Validate(string name, Guid? except)
    {
        var key = NameKey.For(name);
        if (key.Length == 0)
        {
            return "Escribe un nombre.";
        }

        return _all.Any(p => p.Id != except && NameKey.For(p.Name) == key) ? "Ya existe una persona con ese nombre." : null;
    }

    private void Rebuild()
    {
        Rows.Clear();
        var index = 0;
        foreach (var person in _all.OrderBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true)))
        {
            Rows.Add(new PersonRowViewModel(person, index++, _blockCounts.GetValueOrDefault(person.Id), this));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
