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
using Iris.Core.Timing;
using Iris.Features.Services;
using Iris.Shell;

namespace Iris.Features.LiveConsole;

/// <summary>
/// The block-timer strip of the console (IRIS_SPEC §6.9, §7.9): not started → running → finished.
/// Today's changes never touch the template unless "Guardar en la plantilla" is chosen at the end.
/// Only times are saved, never content. The clock never reaches the TV.
/// </summary>
public sealed partial class BlockTimerViewModel : ObservableObject
{
    private readonly ServiceType _type;
    private readonly IServiceTypeRepository _types;
    private readonly IPeopleRepository _peopleRepository;
    private readonly ITimeRecordRepository _records;
    private readonly SignedInNavigator _navigator;
    private readonly Func<DateTime> _clock;
    private readonly DateTime _launchedAt;
    private List<Person> _people;
    private CancellationTokenSource? _ticker;
    private ServiceRecord? _pendingRecord;
    private bool _exitAfterSaving;
    private bool _isRebuildingPending;
    private readonly bool _canSaveRecords;
    private readonly ISyncService? _sync;
    private readonly IUiDispatcher? _ui;

    public BlockTimerViewModel(
        ServiceType type,
        IReadOnlyList<Person> people,
        IServiceTypeRepository types,
        IPeopleRepository peopleRepository,
        ITimeRecordRepository records,
        SignedInNavigator navigator,
        DateTime launchedAt,
        Func<DateTime>? clock = null,
        bool canUpdateTemplate = true,
        bool canSaveRecords = true,
        ISyncService? sync = null,
        IUiDispatcher? ui = null)
    {
        _sync = sync;
        _ui = ui;
        CanUpdateTemplate = canUpdateTemplate;
        _canSaveRecords = canSaveRecords;
        _type = type;
        _people = people.ToList();
        _types = types;
        _peopleRepository = peopleRepository;
        _records = records;
        _navigator = navigator;
        _launchedAt = launchedAt;
        _clock = clock ?? (() => DateTime.Now);
        Timer = new BlockTimer(type.Blocks);
        Now = _clock();

        // Drag and drop in the pending list arrives as remove + add; the timer follows on add.
        PendingBlocks.CollectionChanged += (_, e) =>
        {
            if (!_isRebuildingPending && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                MovePendingBlocks();
            }
        };
    }

    public BlockTimer Timer { get; }

    [ObservableProperty]
    public partial DateTime Now { get; set; }

    public TimerPhase Phase => Timer.Phase;

    public bool IsNotStarted => Phase == TimerPhase.NotStarted;

    public bool IsRunning => Phase == TimerPhase.Running;

    public bool IsFinished => Phase == TimerPhase.Finished;

    /// <summary>"Guardar en la plantilla" needs <see cref="Permission.ServiceTypesManage"/>; without it only "Solo hoy" is offered.</summary>
    public bool CanUpdateTemplate { get; }

    // ===== Not started =====

    public string PlanSummary => IrisDurationFormat.BlocksSummary(Timer.Blocks.Count(b => !b.IsSkipped), Timer.PlannedSeconds / 60);

    public IReadOnlyList<int> PlannedMinutes => Timer.Blocks.Where(b => !b.IsSkipped).Select(b => b.PlannedSeconds / 60).ToList();

    // ===== Running =====

    public TimerBlock? Current => Timer.Current;

    public string CurrentName => Current?.Name.ToUpper(Spanish.Culture) ?? string.Empty;

    public string CurrentLeaderName => LeaderName(Current?.PersonId);

    public int CurrentElapsed => Current is { } c ? Timer.Elapsed(c.Id, Now) : 0;

    public ClockState CurrentClockState => Current is { } c ? BlockTimer.StateFor(CurrentElapsed, c.PlannedSeconds) : ClockState.Normal;

    public string ClockText => IrisDurationFormat.Clock(CurrentElapsed);

    public string PlannedText => Current is { } c ? $"/ {IrisDurationFormat.Clock(c.PlannedSeconds)}" : string.Empty;

    /// <summary>"−21:18" left or "+3:10" over.</summary>
    public string CurrentDeltaText => Current is { } c ? IrisDurationFormat.Delta(CurrentElapsed - c.PlannedSeconds) : string.Empty;

    /// <summary>0…1; full once over.</summary>
    public double CurrentProgress => Current is { PlannedSeconds: > 0 } c ? Math.Min(1, (double)CurrentElapsed / c.PlannedSeconds) : 0;

    public bool IsOnLastBlock => Timer.IsOnLastBlock;

    public string NextButtonText => IsOnLastBlock ? "Terminar" : "Siguiente bloque";

    public bool HasNextBlock => Timer.Next is not null;

    public IReadOnlyList<BlockCrumb> Breadcrumbs
    {
        get
        {
            var blocks = Timer.Blocks;
            return blocks.Select((b, i) =>
            {
                var elapsed = Timer.Elapsed(b.Id, Now);
                return new BlockCrumb(
                    b.Name,
                    b.IsDone ? IrisDurationFormat.Clock(elapsed) : string.Empty,
                    b.IsDone,
                    b.IsRunning,
                    b.IsSkipped,
                    b.IsDone && elapsed > b.PlannedSeconds,
                    i == blocks.Count - 1);
            }).ToList();
        }
    }

    public IReadOnlyList<LeaderMenuItem> CurrentLeaderOptions =>
    [
        new(null, "Sin responsable", Current?.PersonId is null, this),
        .. SortedPeople.Select(p => new LeaderMenuItem(p.Id, p.Name, Current?.PersonId == p.Id, this)),
    ];

    // ===== Finished =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishedSummary))]
    public partial ServiceRecord? FinishedRecord { get; set; }

    [ObservableProperty]
    public partial bool IsRecordSaved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecordError))]
    public partial string? RecordError { get; set; }

    public bool HasRecordError => RecordError is not null;

    /// <summary>The record is saved on this PC but still waits to reach the server (amber "Se enviará cuando haya conexión").</summary>
    [ObservableProperty]
    public partial bool IsRecordPending { get; set; }

    private async Task RefreshPendingAsync()
    {
        if (FinishedRecord is { } record && IsRecordSaved)
        {
            IsRecordPending = await _records.IsPendingAsync(record.Id);
        }
    }

    private void OnSyncStatusChanged(object? sender, SyncStatus status) => _ui?.Post(() => _ = RefreshPendingAsync());
    /// <summary>"Servicio terminado · 1:26:10 (previsto 1:10:00 · +16:10)" or "… · a tiempo)".</summary>
    public string FinishedSummary
    {
        get
        {
            if (FinishedRecord is not { } record)
            {
                return string.Empty;
            }

            var overtime = record.ActualSeconds - record.PlannedSeconds;
            var delta = overtime > 0 ? IrisDurationFormat.Delta(overtime) : "a tiempo";
            return $"Servicio terminado · {LongClock(record.ActualSeconds)} (previsto {LongClock(record.PlannedSeconds)} · {delta})";
        }
    }

    // ===== Sheets =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPickingResponsible), nameof(IsSheetOpen))]
    public partial ResponsiblePickerViewModel? ResponsiblePicker { get; set; }

    public bool IsPickingResponsible => ResponsiblePicker is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddingBlock), nameof(IsSheetOpen))]
    public partial AddBlockViewModel? AddBlockSheet { get; set; }

    public bool IsAddingBlock => AddBlockSheet is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    public partial bool IsEditingPendingBlocks { get; set; }

    public ObservableCollection<PendingBlockViewModel> PendingBlocks { get; } = [];

    public bool HasNoPendingBlocks => PendingBlocks.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    public partial bool IsConfirmingFinish { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    public partial bool IsAskingTemplateUpdate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    public partial bool IsConfirmingExit { get; set; }

    public bool IsSheetOpen => IsPickingResponsible || IsAddingBlock || IsEditingPendingBlocks || IsConfirmingFinish || IsAskingTemplateUpdate || IsConfirmingExit;

    /// <summary>"Agregaste Santa Cena y omitiste Anuncios."</summary>
    public string TemplateChangesMessage => DescribeChanges(Timer.Changes);

    // ===== Flow =====

    /// <summary>"▶ Comenzar": asks who leads the first block.</summary>
    [RelayCommand]
    private void StartBlocks()
    {
        if (Timer.Blocks.FirstOrDefault(b => !b.IsSkipped) is { } first)
        {
            AskLeader(first, personId =>
            {
                Timer.Start(personId, _clock());
                Changed();
                RunBlockClock();
            });
        }
    }

    /// <summary>"Siguiente bloque →": the next leader is picked first; confirming closes the current block. On the last block it finishes.</summary>
    [RelayCommand]
    private void GoToNextBlock()
    {
        if (!IsRunning)
        {
            return;
        }

        if (Timer.Next is not { } next)
        {
            RequestFinish();
            return;
        }

        AskLeader(next, personId =>
        {
            Timer.Advance(personId, _clock());
            Changed();
        });
    }

    [RelayCommand]
    private void CloseResponsiblePicker() => ResponsiblePicker = null;

    [RelayCommand]
    private void SetLeader(LeaderMenuItem item)
    {
        if (Current is { } current)
        {
            Timer.SetPerson(current.Id, item.PersonId);
            Changed();
        }
    }

    [RelayCommand]
    private void PresentAddBlock() => AddBlockSheet = new AddBlockViewModel(_people, (name, minutes, personId) =>
    {
        Timer.AddBlock(name, minutes, personId);
        AddBlockSheet = null;
        Changed();
    });

    [RelayCommand]
    private void CloseAddBlock() => AddBlockSheet = null;

    [RelayCommand]
    private void SkipNextBlock()
    {
        Timer.SkipNext();
        Changed();
    }

    [RelayCommand]
    private void PresentPendingEditor()
    {
        RebuildPending();
        IsEditingPendingBlocks = true;
    }

    [RelayCommand]
    private void ClosePendingEditor()
    {
        IsEditingPendingBlocks = false;
        Changed();
    }

    public void RenamePendingBlock(Guid id, string name)
    {
        Timer.Rename(id, name);
        Changed();
    }

    public void SetPendingMinutes(Guid id, int minutes)
    {
        Timer.SetMinutes(id, minutes);
        Changed();
    }

    public void SetPendingLeader(Guid id, Guid? personId)
    {
        Timer.SetPerson(id, personId);
        Changed();
    }

    [RelayCommand]
    private void ToggleSkip(PendingBlockViewModel row)
    {
        Timer.SetSkipped(row.Id, !row.IsSkipped);
        row.Sync(Timer.Block(row.Id));
        Changed();
    }

    /// <summary>Drag-and-drop in "Bloques pendientes" reorders the rows; the timer follows.</summary>
    public void MovePendingBlocks()
    {
        Timer.ReorderPending(PendingBlocks.Select(p => p.Id).ToList());
        Changed();
    }

    [RelayCommand]
    private void RequestFinish()
    {
        if (IsRunning)
        {
            IsConfirmingFinish = true;
        }
    }

    [RelayCommand]
    private void CancelFinish() => IsConfirmingFinish = false;

    /// <summary>Closes the current block; asks about the template only when today's blocks changed.</summary>
    [RelayCommand]
    private async Task FinishServiceAsync()
    {
        IsConfirmingFinish = false;
        Timer.Finish(_clock());
        StopClock();
        _pendingRecord = Timer.Record(_type.Id, _launchedAt, PeopleNames(), _type.Name);
        FinishedRecord = _pendingRecord;
        Changed();
        if (Timer.HasTemplateChanges)
        {
            OnPropertyChanged(nameof(TemplateChangesMessage));
            IsAskingTemplateUpdate = true;
        }
        else
        {
            await SaveRecordAsync(false);
        }
    }

    [RelayCommand]
    private Task SaveOnlyToday() => SaveRecordAsync(false);

    [RelayCommand]
    private Task SaveToTemplate() => SaveRecordAsync(true);

    public async Task SaveRecordAsync(bool updatingTemplate)
    {
        IsAskingTemplateUpdate = false;
        if (_pendingRecord is not { } record)
        {
            return;
        }

        RecordError = null;
        if (!_canSaveRecords)
        {
            RecordError = "No tienes permiso para guardar los tiempos.";
            return;
        }

        try
        {
            if (updatingTemplate && CanUpdateTemplate)
            {
                await _types.SaveAsync(_type with { Blocks = Timer.UpdatedTemplate(_type.Blocks) });
            }

            await _records.SaveAsync(record);
            IsRecordSaved = true;
            if (_sync is not null)
            {
                _sync.StatusChanged -= OnSyncStatusChanged;
                _sync.StatusChanged += OnSyncStatusChanged;
            }

            await RefreshPendingAsync();
            if (_exitAfterSaving)
            {
                _navigator.GoHome();
            }
        }
        catch
        {
            RecordError = "No pudimos guardar los tiempos. Inténtalo de nuevo.";
        }
    }

    [RelayCommand]
    private Task RetrySavingRecord() => SaveRecordAsync(false);

    [RelayCommand]
    private void ViewInTimes()
    {
        if (FinishedRecord is { } record)
        {
            _navigator.PendingRecordId = record.Id;
            _navigator.Open(AppRoute.Times);
        }
    }

    // ===== Exit ("‹ Inicio" while running) =====

    /// <summary>Returns true when leaving can go ahead now; otherwise asks first.</summary>
    public bool RequestExit()
    {
        if (!IsRunning)
        {
            return true;
        }

        IsConfirmingExit = true;
        return false;
    }

    [RelayCommand]
    private async Task FinishAndExitAsync()
    {
        IsConfirmingExit = false;
        _exitAfterSaving = true;
        await FinishServiceAsync();
    }

    [RelayCommand]
    private void ExitWithoutSaving()
    {
        IsConfirmingExit = false;
        StopClock();
        _navigator.GoHome();
    }

    [RelayCommand]
    private void CancelExit() => IsConfirmingExit = false;

    public void Stop()
    {
        StopClock();
        if (_sync is not null)
        {
            _sync.StatusChanged -= OnSyncStatusChanged;
        }
    }

    // ===== Helpers =====

    private void AskLeader(TimerBlock block, Action<Guid?> then)
    {
        ResponsiblePicker = new ResponsiblePickerViewModel(block.Name, block.PersonId, _people, AddPersonAsync, personId =>
        {
            ResponsiblePicker = null;
            then(personId);
        });
    }

    private async Task<Person> AddPersonAsync(string name)
    {
        var person = await _peopleRepository.AddAsync(name);
        _people.Add(person);
        return person;
    }

    /// <summary>1 s tick, only while a block runs.</summary>
    private async void RunBlockClock()
    {
        StopClock();
        var cts = _ticker = new CancellationTokenSource();
        try
        {
            while (!cts.IsCancellationRequested && IsRunning)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                Now = _clock();
                RaiseClock();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopClock()
    {
        _ticker?.Cancel();
        _ticker = null;
    }

    private void RaiseClock()
    {
        foreach (var name in ClockProperties)
        {
            OnPropertyChanged(name);
        }
    }

    private void Changed()
    {
        Now = _clock();
        foreach (var name in DerivedProperties)
        {
            OnPropertyChanged(name);
        }

        if (IsEditingPendingBlocks)
        {
            SyncPending();
        }
    }

    private void RebuildPending()
    {
        _isRebuildingPending = true;
        PendingBlocks.Clear();
        var leaders = LeaderOptions();
        foreach (var block in Timer.Pending)
        {
            PendingBlocks.Add(new PendingBlockViewModel(block, leaders, this));
        }

        _isRebuildingPending = false;
        OnPropertyChanged(nameof(HasNoPendingBlocks));
    }

    private void SyncPending()
    {
        foreach (var row in PendingBlocks)
        {
            if (Timer.Blocks.FirstOrDefault(b => b.Id == row.Id) is { IsPending: true } block)
            {
                row.Sync(block);
            }
        }
    }

    private IReadOnlyList<LeaderOption> LeaderOptions() =>
        [LeaderOption.Nobody, .. SortedPeople.Select(p => new LeaderOption(p.Id, p.Name))];

    private IEnumerable<Person> SortedPeople => _people.OrderBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true));

    private string LeaderName(Guid? id) => id is { } value ? _people.FirstOrDefault(p => p.Id == value)?.Name ?? "Persona eliminada" : "Sin responsable";

    private IReadOnlyDictionary<Guid, string> PeopleNames() => _people.ToDictionary(p => p.Id, p => p.Name);

    /// <summary>"1:10:00" — the long form used in the finished summary.</summary>
    private static string LongClock(int seconds) => $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}";

    public static string DescribeChanges(TemplateChanges changes)
    {
        var parts = new List<string>();
        if (changes.Added.Count > 0)
        {
            parts.Add($"agregaste {JoinNames(changes.Added)}");
        }

        if (changes.Skipped.Count > 0)
        {
            parts.Add($"omitiste {JoinNames(changes.Skipped)}");
        }

        if (changes.Edited.Count > 0)
        {
            parts.Add($"cambiaste {JoinNames(changes.Edited)}");
        }

        if (changes.IsReordered)
        {
            parts.Add("cambiaste el orden");
        }

        return parts.Count == 0 ? string.Empty : Spanish.Capitalize(JoinNames(parts)) + ".";
    }

    private static string JoinNames(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} y {items[^1]}",
    };

    private static readonly string[] ClockProperties =
    [
        nameof(CurrentElapsed), nameof(CurrentClockState), nameof(ClockText), nameof(CurrentDeltaText), nameof(CurrentProgress),
    ];

    private static readonly string[] DerivedProperties =
    [
        nameof(Phase), nameof(IsNotStarted), nameof(IsRunning), nameof(IsFinished), nameof(PlanSummary), nameof(PlannedMinutes),
        nameof(Current), nameof(CurrentName), nameof(CurrentLeaderName), nameof(CurrentElapsed), nameof(CurrentClockState),
        nameof(ClockText), nameof(PlannedText), nameof(CurrentDeltaText), nameof(CurrentProgress), nameof(IsOnLastBlock),
        nameof(NextButtonText), nameof(HasNextBlock), nameof(Breadcrumbs), nameof(CurrentLeaderOptions), nameof(TemplateChangesMessage),
    ];
}
