using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Iris.Core.Formatting;
using Iris.Core.Models;

namespace Iris.Features.Times;

public enum FilterGroup
{
    RecordType,
    Period,
    ServiceType,
    Block,
    Person,
}

/// <summary>An entry of a filter menu (pill + flyout).</summary>
public sealed record FilterOption(FilterGroup Group, object? Key, string Label, bool IsSelected, TimesViewModel Owner);

/// <summary>A record row in the month list: color dot, service, date, total and "+16:10" / "A tiempo".</summary>
public sealed partial class RecordRowViewModel(ServiceRecord record, string typeName, string color, TimesViewModel owner) : ObservableObject
{
    public ServiceRecord Record { get; } = record;

    public TimesViewModel Owner { get; } = owner;

    public string TypeName { get; } = typeName;

    public string Color { get; } = color;

    public string DateText => Spanish.ShortDate(Record.Date);

    public string DurationText => IrisDurationFormat.Clock(Record.ActualSeconds);

    public bool IsOver => Record.OvertimeSeconds > 0;

    public string DeltaText => IsOver ? IrisDurationFormat.Delta(Record.OvertimeSeconds) : "A tiempo";

    public string AccessibleName => $"{TypeName}, {Spanish.LongDate(Record.Date)}, {DurationText}, {DeltaText}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>A month group ("SEPTIEMBRE 2026") of record rows.</summary>
public sealed record RecordGroup(string Header, IReadOnlyList<RecordRowViewModel> Rows);

/// <summary>A block in the record detail: leader, time bar on a common scale, real and delta, chips.</summary>
public sealed record BlockDetailRow(BlockRecord Block, string Leader, double Scale, TimesViewModel Owner)
{
    public string Name => Block.Name;

    public bool IsSkipped => Block.Status == BlockStatus.Skipped;

    public bool IsCounted => !IsSkipped;

    /// <summary>The "•••" menu (adjust duration, change leader) needs <see cref="Iris.Core.Models.Permission.RecordsManage"/>.</summary>
    public bool ShowsMenu => IsCounted && Owner.CanManage;

    public bool IsAdjusted => Block.Status == BlockStatus.Adjusted;

    public bool IsOver => Block.IsOver;

    public string ActualText => IrisDurationFormat.Clock(Block.ActualSeconds);

    public string DeltaText => IsOver ? IrisDurationFormat.Delta(Block.Overtime) : "a tiempo";

    public string PlannedText => $"previsto {IrisDurationFormat.Clock(Block.PlannedSeconds)}";
}

/// <summary>"Ajustar duración": minutes and seconds wheels → the block becomes "Ajustado".</summary>
public sealed partial class DurationAdjustmentViewModel(Guid recordId, BlockRecord block) : ObservableObject
{
    public Guid RecordId { get; } = recordId;

    public BlockRecord Block { get; } = block;

    public string Title => Block.Name;

    public string Subtitle => $"previsto {IrisDurationFormat.Clock(Block.PlannedSeconds)}";

    [ObservableProperty]
    public partial double Minutes { get; set; } = block.ActualSeconds / 60;

    [ObservableProperty]
    public partial double Seconds { get; set; } = block.ActualSeconds % 60;

    public int TotalSeconds => (int)(Math.Clamp(double.IsNaN(Minutes) ? 0 : Math.Round(Minutes), 0, 600) * 60 + Math.Clamp(double.IsNaN(Seconds) ? 0 : Math.Round(Seconds), 0, 59));
}

/// <summary>A choice in "Cambiar responsable".</summary>
public sealed record LeaderChoice(Person? Person, string Name, bool IsSelected, TimesViewModel Owner);

/// <summary>A row of POR PERSONA.</summary>
public sealed record PersonStatRow(Guid PersonId, string Name, string Initials, int ColorIndex, string Participations, string TimesOver, string AverageOver, string MaxOver, string TotalOver, TimesViewModel Owner);

/// <summary>A row of POR BLOQUE.</summary>
public sealed record BlockStatRow(string Name, string TimesOverText, string AverageOver, double AverageActual, double AveragePlanned, double Scale, string ActualVsPlanned, int ColorIndex);

/// <summary>A block in the person detail sheet.</summary>
public sealed record PersonBlockRow(string Date, string Service, string Block, string ActualVsPlanned, string Overtime, bool IsOver);

/// <summary>A KPI card.</summary>
public sealed record KpiCard(string Title, string Value, string? Detail)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}
