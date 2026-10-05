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
using Iris.Core.Timing;
using Iris.Shell;

namespace Iris.Features.Times;

/// <summary>
/// Tiempos (IRIS_SPEC §6.10, §7.10): records by month with detail, duration adjustment, leader
/// change and deletion; summaries with filters, KPIs and tables by person and by block. Neutral
/// language, no rankings; only visible in the app.
/// </summary>
public sealed partial class TimesViewModel : ObservableObject
{
    private readonly ITimeRecordRepository _records;
    private readonly IServiceTypeRepository _types;
    private readonly IPeopleRepository _people;
    private readonly SignedInNavigator _navigator;
    private readonly Func<DateTime> _now;
    private List<ServiceRecord> _all = [];
    private IReadOnlyList<ServiceType> _typeList = [];
    private IReadOnlyList<Person> _peopleList = [];
    private TimeStatistics? _stats;

    public TimesViewModel(ITimeRecordRepository records, IServiceTypeRepository types, IPeopleRepository people, SignedInNavigator navigator, Func<DateTime>? now = null)
    {
        _records = records;
        _types = types;
        _people = people;
        _navigator = navigator;
        _now = now ?? (() => DateTime.Now);
    }

    public IList<string> Tabs { get; } = ["Registros", "Resúmenes"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecordsTab), nameof(IsSummaryTab))]
    public partial int TabIndex { get; set; }

    public bool IsRecordsTab => HasRecords && TabIndex == 0;

    public bool IsSummaryTab => HasRecords && TabIndex == 1;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public bool HasRecords => _all.Count > 0;

    public bool IsEmpty => !IsLoading && _all.Count == 0;

    public IReadOnlyList<ServiceRecord> Records => _all;

    [RelayCommand]
    private void GoHome() => _navigator.GoHome();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        var records = _records.RecordsAsync();
        var types = _types.ServiceTypesAsync();
        var people = _people.PeopleAsync();
        _all = (await records).ToList();
        _typeList = await types;
        _peopleList = await people;
        IsLoading = false;

        if (_navigator.PendingRecordId is { } pending)
        {
            _navigator.PendingRecordId = null;
            TabIndex = 0;
            RecordTypeFilter = null;
            SelectedRecordId = pending;
        }

        RefreshAll();
    }

    partial void OnTabIndexChanged(int value) => RefreshAll();

    // ===================== Registros =====================

    [ObservableProperty]
    public partial Guid? RecordTypeFilter { get; set; }

    [ObservableProperty]
    public partial Guid? SelectedRecordId { get; set; }

    public string RecordTypeFilterText => RecordTypeFilter is { } id ? TypeName(id) : "Todos los servicios";

    public IReadOnlyList<FilterOption> RecordTypeOptions => TypeOptions(FilterGroup.RecordType, RecordTypeFilter);

    public ObservableCollection<RecordGroup> Groups { get; } = [];

    public ServiceRecord? SelectedRecord => _all.FirstOrDefault(r => r.Id == SelectedRecordId);

    public bool HasSelection => SelectedRecord is not null;

    public bool HasNoSelection => HasRecords && SelectedRecord is null;

    public string DetailTitle => SelectedRecord is { } r ? TypeName(r.ServiceTypeId) : string.Empty;

    public string DetailDate => SelectedRecord is { } r ? Spanish.LongDate(r.Date) : string.Empty;

    public string DetailDuration => SelectedRecord is { } r ? IrisDurationFormat.Clock(r.ActualSeconds) : string.Empty;

    public string DetailPlanned => SelectedRecord is { } r ? IrisDurationFormat.Clock(r.PlannedSeconds) : string.Empty;

    public bool DetailIsOver => SelectedRecord?.OvertimeSeconds > 0;

    public string DetailOvertime => SelectedRecord is { OvertimeSeconds: > 0 } r ? IrisDurationFormat.Delta(r.OvertimeSeconds) : "A tiempo";

    public IReadOnlyList<BlockDetailRow> DetailBlocks
    {
        get
        {
            if (SelectedRecord is not { } record)
            {
                return [];
            }

            double scale = record.Blocks.Select(b => Math.Max(b.ActualSeconds, b.PlannedSeconds)).DefaultIfEmpty(1).Max();
            return record.Blocks.Select(b => new BlockDetailRow(b, LeaderName(b.PersonId, b.PersonName), scale, this)).ToList();
        }
    }

    [RelayCommand]
    private void SelectRecord(RecordRowViewModel row) => SelectedRecordId = row.Record.Id;

    partial void OnSelectedRecordIdChanged(Guid? value) => RefreshRecords();

    partial void OnRecordTypeFilterChanged(Guid? value) => RefreshRecords();

    // ----- Ajustar duración -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAdjusting), nameof(IsModalOpen))]
    public partial DurationAdjustmentViewModel? Adjustment { get; set; }

    public bool IsAdjusting => Adjustment is not null;

    [RelayCommand]
    private void BeginAdjustment(BlockDetailRow row)
    {
        if (SelectedRecord is { } record)
        {
            Adjustment = new DurationAdjustmentViewModel(record.Id, row.Block);
        }
    }

    [RelayCommand]
    private void CancelAdjustment() => Adjustment = null;

    /// <summary>Saves the new actual time; the block becomes "Ajustado".</summary>
    [RelayCommand]
    private async Task AdjustDurationAsync()
    {
        if (Adjustment is not { } adjustment)
        {
            return;
        }

        Adjustment = null;
        await UpdateBlockAsync(adjustment.RecordId, adjustment.Block.Id, b => b with { ActualSeconds = adjustment.TotalSeconds, Status = BlockStatus.Adjusted });
    }

    // ----- Cambiar responsable -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangingLeader), nameof(IsModalOpen), nameof(LeaderChoices), nameof(LeaderSheetTitle))]
    public partial BlockDetailRow? LeaderTarget { get; set; }

    public bool IsChangingLeader => LeaderTarget is not null;

    public string LeaderSheetTitle => LeaderTarget is { } row ? $"¿Quién dirigió {row.Name}?" : string.Empty;

    public IReadOnlyList<LeaderChoice> LeaderChoices => LeaderTarget is not { } row
        ? []
        : [
            new(null, "Sin responsable", row.Block.PersonId is null, this),
            .. SortedPeople.Select(p => new LeaderChoice(p, p.Name, row.Block.PersonId == p.Id, this)),
        ];

    [RelayCommand]
    private void BeginLeaderChange(BlockDetailRow row) => LeaderTarget = row;

    [RelayCommand]
    private void CancelLeaderChange() => LeaderTarget = null;

    /// <summary>Updates the stored id and name.</summary>
    [RelayCommand]
    private async Task ChangeLeaderAsync(LeaderChoice choice)
    {
        if (LeaderTarget is not { } row || SelectedRecord is not { } record)
        {
            return;
        }

        LeaderTarget = null;
        await UpdateBlockAsync(record.Id, row.Block.Id, b => b with { PersonId = choice.Person?.Id, PersonName = choice.Person?.Name });
    }

    // ----- Eliminar registro -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    public partial bool IsConfirmingDelete { get; set; }

    [RelayCommand]
    private void RequestDeleteSelected() => IsConfirmingDelete = SelectedRecord is not null;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        IsConfirmingDelete = false;
        if (SelectedRecord is not { } record)
        {
            return;
        }

        try
        {
            await _records.DeleteAsync(record.Id);
            _all.Remove(record);
            SelectedRecordId = null;
            RefreshAll();
        }
        catch
        {
            // The record stays; nothing else to undo.
        }
    }

    private async Task UpdateBlockAsync(Guid recordId, Guid blockId, Func<BlockRecord, BlockRecord> update)
    {
        var index = _all.FindIndex(r => r.Id == recordId);
        if (index < 0)
        {
            return;
        }

        var record = _all[index];
        var updated = record with { Blocks = record.Blocks.Select(b => b.Id == blockId ? update(b) : b).ToList() };
        try
        {
            await _records.SaveAsync(updated);
            _all[index] = updated;
            RefreshAll();
        }
        catch
        {
            // Keep the previous values on failure.
        }
    }

    private void RefreshRecords()
    {
        var filtered = _all
            .Where(r => RecordTypeFilter is null || r.ServiceTypeId == RecordTypeFilter)
            .OrderByDescending(r => r.Date)
            .ToList();
        if (SelectedRecordId is { } selected && filtered.All(r => r.Id != selected))
        {
            SelectedRecordId = filtered.FirstOrDefault()?.Id;
            return;
        }

        if (SelectedRecordId is null && filtered.Count > 0)
        {
            SelectedRecordId = filtered[0].Id;
            return;
        }

        Groups.Clear();
        foreach (var month in filtered.GroupBy(r => (r.Date.Year, r.Date.Month)))
        {
            Groups.Add(new RecordGroup(
                Spanish.MonthYear(month.Key.Year, month.Key.Month).ToUpper(Spanish.Culture),
                month.Select(r => new RecordRowViewModel(r, TypeName(r.ServiceTypeId), TypeColor(r.ServiceTypeId), this) { IsSelected = r.Id == SelectedRecordId }).ToList()));
        }

        foreach (var name in RecordProperties)
        {
            OnPropertyChanged(name);
        }
    }

    // ===================== Resúmenes =====================

    [ObservableProperty]
    public partial SummaryFilter SummaryFilter { get; set; } = SummaryFilter.Default;

    partial void OnSummaryFilterChanged(SummaryFilter value) => RefreshSummary();

    public string PeriodText => SummaryFilter.Period.Kind switch
    {
        PeriodKind.ThisMonth => "Este mes",
        PeriodKind.LastMonth => "Mes anterior",
        PeriodKind.Last3Months => "Últimos 3 meses",
        PeriodKind.ThisYear => "Este año",
        PeriodKind.All => "Todo",
        _ => Spanish.Capitalize(Spanish.MonthYear(SummaryFilter.Period.Year, SummaryFilter.Period.MonthNumber)),
    };

    public string ServiceFilterText => SummaryFilter.ServiceTypeId is { } id ? TypeName(id) : "Todos los servicios";

    public string BlockFilterText => SummaryFilter.BlockName ?? "Todos los bloques";

    public string PersonFilterText => SummaryFilter.PersonId is { } id ? LeaderName(id, null) : "Todas las personas";

    public IReadOnlyList<FilterOption> PeriodOptions =>
    [
        .. new[] { PeriodKind.ThisMonth, PeriodKind.LastMonth, PeriodKind.Last3Months, PeriodKind.ThisYear, PeriodKind.All }
            .Select(k => new FilterOption(FilterGroup.Period, k, PeriodLabel(k), SummaryFilter.Period.Kind == k, this)),
        new FilterOption(FilterGroup.Period, PeriodKind.Month, "Elegir mes…", SummaryFilter.Period.Kind == PeriodKind.Month, this),
    ];

    public IReadOnlyList<FilterOption> ServiceOptions => TypeOptions(FilterGroup.ServiceType, SummaryFilter.ServiceTypeId);

    public IReadOnlyList<FilterOption> BlockOptions =>
    [
        new(FilterGroup.Block, null, "Todos los bloques", SummaryFilter.BlockName is null, this),
        .. TimeStatistics.BlockNames(_all).Select(n => new FilterOption(FilterGroup.Block, n, n, NameKey.For(n) == NameKey.For(SummaryFilter.BlockName), this)),
    ];

    public IReadOnlyList<FilterOption> PersonOptions =>
    [
        new(FilterGroup.Person, null, "Todas las personas", SummaryFilter.PersonId is null, this),
        .. SortedPeople.Select(p => new FilterOption(FilterGroup.Person, p.Id, p.Name, SummaryFilter.PersonId == p.Id, this)),
    ];

    public bool HasSummaryData => _stats is { IsEmpty: false };

    public bool HasNoSummaryData => _stats is { IsEmpty: true };

    public IReadOnlyList<KpiCard> Kpis
    {
        get
        {
            if (_stats is not { } s)
            {
                return [];
            }

            var (over, total) = s.OverBlocks;
            var percent = total == 0 ? 0 : (int)Math.Round(100.0 * over / total);
            return
            [
                new("Servicios", s.ServiceCount.ToString(Spanish.Culture), null),
                new("Duración promedio", IrisDurationFormat.Clock(s.AverageDuration), null),
                new("Exceso promedio por servicio", s.AverageOvertimePerService > 0 ? IrisDurationFormat.Delta(s.AverageOvertimePerService) : "A tiempo", null),
                new("Bloques pasados", $"{over} de {total}", $"{percent} %"),
            ];
        }
    }

    public IReadOnlyList<PersonStatRow> ByPerson => _stats?.ByPerson
        .Select((p, i) => new PersonStatRow(
            p.PersonId,
            LeaderName(p.PersonId, p.StoredName),
            PersonInitials.From(LeaderName(p.PersonId, p.StoredName)),
            i,
            p.Participations.ToString(Spanish.Culture),
            p.TimesOver.ToString(Spanish.Culture),
            OvertimeText(p.AverageOvertimeWhenOver),
            OvertimeText(p.MaxOvertime),
            OvertimeText(p.TotalOvertime),
            this))
        .ToList() ?? [];

    public IReadOnlyList<BlockStatRow> ByBlock
    {
        get
        {
            if (_stats is not { } s)
            {
                return [];
            }

            double scale = s.ByBlock.Select(b => Math.Max(b.AverageActual, b.AveragePlanned)).DefaultIfEmpty(1).Max();
            return s.ByBlock.Select((b, i) => new BlockStatRow(
                b.Name,
                $"Se pasó {b.TimesOver} de {b.Total} {(b.Total == 1 ? "vez" : "veces")}",
                OvertimeText(b.AverageOvertimeWhenOver),
                b.AverageActual,
                b.AveragePlanned,
                scale,
                $"{IrisDurationFormat.Clock(b.AverageActual)} / {IrisDurationFormat.Clock(b.AveragePlanned)}",
                i)).ToList();
        }
    }

    [RelayCommand]
    private void ChooseFilter(FilterOption option)
    {
        switch (option.Group)
        {
            case FilterGroup.RecordType:
                RecordTypeFilter = option.Key as Guid?;
                break;
            case FilterGroup.Period when option.Key is PeriodKind.Month:
                BeginPickingMonth();
                break;
            case FilterGroup.Period when option.Key is PeriodKind kind:
                SummaryFilter = SummaryFilter with { Period = new SummaryPeriod(kind) };
                break;
            case FilterGroup.ServiceType:
                SummaryFilter = SummaryFilter with { ServiceTypeId = option.Key as Guid? };
                break;
            case FilterGroup.Block:
                SummaryFilter = SummaryFilter with { BlockName = option.Key as string };
                break;
            case FilterGroup.Person:
                SummaryFilter = SummaryFilter with { PersonId = option.Key as Guid? };
                break;
        }
    }

    // ----- Elegir mes… -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModalOpen))]
    public partial bool IsPickingMonth { get; set; }

    public IReadOnlyList<string> MonthNames { get; } = Enumerable.Range(1, 12).Select(m => Spanish.Capitalize(Spanish.Month(m))).ToList();

    public IReadOnlyList<int> Years => Enumerable.Range(_now().Year - 5, 6).Reverse().ToList();

    [ObservableProperty]
    public partial int PickedYear { get; set; }

    /// <summary>0-based for the month ComboBox.</summary>
    [ObservableProperty]
    public partial int PickedMonthIndex { get; set; }

    public void BeginPickingMonth()
    {
        var period = SummaryFilter.Period;
        var now = _now();
        PickedYear = period.Kind == PeriodKind.Month ? period.Year : now.Year;
        PickedMonthIndex = (period.Kind == PeriodKind.Month ? period.MonthNumber : now.Month) - 1;
        IsPickingMonth = true;
    }

    [RelayCommand]
    private void CancelPickingMonth() => IsPickingMonth = false;

    [RelayCommand]
    private void ConfirmPickedMonth()
    {
        IsPickingMonth = false;
        SummaryFilter = SummaryFilter with { Period = new SummaryPeriod(PeriodKind.Month, PickedYear, Math.Clamp(PickedMonthIndex, 0, 11) + 1) };
    }

    // ----- Person detail -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingPersonDetail), nameof(IsModalOpen), nameof(PersonDetailName), nameof(PersonDetailSummary), nameof(PersonDetailBlocks))]
    public partial PersonStatRow? PersonDetail { get; set; }

    public bool IsShowingPersonDetail => PersonDetail is not null;

    public string PersonDetailName => PersonDetail?.Name ?? string.Empty;

    /// <summary>"Se pasó en 5 de 8 bloques · promedio +6:20" or "A tiempo en sus 8 bloques".</summary>
    public string PersonDetailSummary
    {
        get
        {
            if (PersonDetail is not { } row || _stats is null)
            {
                return string.Empty;
            }

            var blocks = _stats.BlocksOf(row.PersonId);
            var over = blocks.Where(e => e.Block.IsOver).ToList();
            return over.Count == 0
                ? $"A tiempo en sus {blocks.Count} bloques"
                : $"Se pasó en {over.Count} de {blocks.Count} bloques · promedio {IrisDurationFormat.Delta((int)Math.Round(over.Average(e => e.Block.Overtime)))}";
        }
    }

    public IReadOnlyList<PersonBlockRow> PersonDetailBlocks => PersonDetail is { } row && _stats is not null
        ? _stats.BlocksOf(row.PersonId).Select(e => new PersonBlockRow(
            Spanish.ShortDate(e.Record.Date),
            TypeName(e.Record.ServiceTypeId),
            e.Block.Name,
            $"{IrisDurationFormat.Clock(e.Block.ActualSeconds)} / {IrisDurationFormat.Clock(e.Block.PlannedSeconds)}",
            e.Block.IsOver ? IrisDurationFormat.Delta(e.Block.Overtime) : "a tiempo",
            e.Block.IsOver)).ToList()
        : [];

    [RelayCommand]
    private void ShowPersonDetail(PersonStatRow row) => PersonDetail = row;

    [RelayCommand]
    private void ClosePersonDetail() => PersonDetail = null;

    public bool IsModalOpen => IsAdjusting || IsChangingLeader || IsConfirmingDelete || IsPickingMonth || IsShowingPersonDetail;

    private void RefreshSummary()
    {
        _stats = new TimeStatistics(_all, SummaryFilter, _now());
        foreach (var name in SummaryProperties)
        {
            OnPropertyChanged(name);
        }
    }

    // ===================== Helpers =====================

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsRecordsTab));
        OnPropertyChanged(nameof(IsSummaryTab));
        RefreshRecords();
        RefreshSummary();
    }

    private IEnumerable<Person> SortedPeople => _peopleList.OrderBy(p => p.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true));

    private IReadOnlyList<FilterOption> TypeOptions(FilterGroup group, Guid? selected) =>
    [
        new(group, null, "Todos los servicios", selected is null, this),
        .. _typeList.Select(t => new FilterOption(group, t.Id, t.Name, selected == t.Id, this)),
    ];

    /// <summary>A deleted type shows "Servicio eliminado".</summary>
    private string TypeName(Guid id) => _typeList.FirstOrDefault(t => t.Id == id)?.Name ?? "Servicio eliminado";

    private string TypeColor(Guid id) => _typeList.FirstOrDefault(t => t.Id == id)?.Color ?? "#F6F3EE";

    /// <summary>Current name; else the name saved in the record; else "Persona eliminada". No person: "Sin responsable".</summary>
    private string LeaderName(Guid? id, string? storedName) => id is not { } value
        ? "Sin responsable"
        : _peopleList.FirstOrDefault(p => p.Id == value)?.Name ?? storedName ?? FindStoredName(value) ?? "Persona eliminada";

    private string? FindStoredName(Guid id) => _all.SelectMany(r => r.Blocks).FirstOrDefault(b => b.PersonId == id && b.PersonName is not null)?.PersonName;

    private static string OvertimeText(int seconds) => seconds > 0 ? IrisDurationFormat.Delta(seconds) : "—";

    private static string PeriodLabel(PeriodKind kind) => kind switch
    {
        PeriodKind.ThisMonth => "Este mes",
        PeriodKind.LastMonth => "Mes anterior",
        PeriodKind.Last3Months => "Últimos 3 meses",
        PeriodKind.ThisYear => "Este año",
        _ => "Todo",
    };

    private static readonly string[] RecordProperties =
    [
        nameof(RecordTypeFilterText), nameof(RecordTypeOptions), nameof(SelectedRecord), nameof(HasSelection), nameof(HasNoSelection),
        nameof(DetailTitle), nameof(DetailDate), nameof(DetailDuration), nameof(DetailPlanned), nameof(DetailIsOver), nameof(DetailOvertime),
        nameof(DetailBlocks),
    ];

    private static readonly string[] SummaryProperties =
    [
        nameof(PeriodText), nameof(ServiceFilterText), nameof(BlockFilterText), nameof(PersonFilterText), nameof(PeriodOptions),
        nameof(ServiceOptions), nameof(BlockOptions), nameof(PersonOptions), nameof(HasSummaryData), nameof(HasNoSummaryData),
        nameof(Kpis), nameof(ByPerson), nameof(ByBlock), nameof(PersonDetailSummary), nameof(PersonDetailBlocks),
    ];
}
