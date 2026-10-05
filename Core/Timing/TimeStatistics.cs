using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Formatting;
using Iris.Core.Models;

namespace Iris.Core.Timing;

public enum PeriodKind
{
    ThisMonth,
    LastMonth,
    Last3Months,
    ThisYear,
    All,
    Month,
}

/// <summary>A summary period by calendar month. <see cref="Year"/>/<see cref="MonthNumber"/> only for <see cref="PeriodKind.Month"/>.</summary>
public sealed record SummaryPeriod(PeriodKind Kind, int Year = 0, int MonthNumber = 0)
{
    public static readonly SummaryPeriod Default = new(PeriodKind.Last3Months);

    /// <summary>[start, end) for <paramref name="now"/>; null for "Todo".</summary>
    public (DateTime Start, DateTime End)? Range(DateTime now)
    {
        var month = new DateTime(now.Year, now.Month, 1);
        return Kind switch
        {
            PeriodKind.ThisMonth => (month, month.AddMonths(1)),
            PeriodKind.LastMonth => (month.AddMonths(-1), month),
            PeriodKind.Last3Months => (month.AddMonths(-2), month.AddMonths(1)),
            PeriodKind.ThisYear => (new DateTime(now.Year, 1, 1), new DateTime(now.Year + 1, 1, 1)),
            PeriodKind.Month => (new DateTime(Year, MonthNumber, 1), new DateTime(Year, MonthNumber, 1).AddMonths(1)),
            _ => null,
        };
    }

    public bool Contains(DateTime date, DateTime now) => Range(now) is not { } range || (date >= range.Start && date < range.End);
}

public sealed record SummaryFilter(SummaryPeriod Period, Guid? ServiceTypeId = null, string? BlockName = null, Guid? PersonId = null)
{
    public static readonly SummaryFilter Default = new(SummaryPeriod.Default);
}

/// <summary>A counted block that passed the filters, with its service.</summary>
public sealed record StatEntry(ServiceRecord Record, BlockRecord Block);

public sealed record PersonStats(Guid PersonId, string? StoredName, int Participations, int TimesOver, int AverageOvertimeWhenOver, int MaxOvertime, int TotalOvertime);

public sealed record BlockStats(string Name, int TimesOver, int Total, int AverageOvertimeWhenOver, int AverageActual, int AveragePlanned);

/// <summary>
/// Pure summary math (IRIS_SPEC §7.10). Skipped blocks never count; adjusted ones do. A service
/// counts when at least one of its blocks passes the filters; averages are per service over the
/// counted blocks. Overtime has no tolerance.
/// </summary>
public sealed class TimeStatistics
{
    public TimeStatistics(IEnumerable<ServiceRecord> records, SummaryFilter filter, DateTime now)
    {
        var blockKey = NameKey.For(filter.BlockName);
        Entries = records
            .Where(r => filter.Period.Contains(r.Date, now))
            .Where(r => filter.ServiceTypeId is null || r.ServiceTypeId == filter.ServiceTypeId)
            .SelectMany(r => r.Blocks.Select(b => new StatEntry(r, b)))
            .Where(e => e.Block.IsCounted)
            .Where(e => blockKey.Length == 0 || NameKey.For(e.Block.Name) == blockKey)
            .Where(e => filter.PersonId is null || e.Block.PersonId == filter.PersonId)
            .ToList();

        var services = Entries.GroupBy(e => e.Record.Id).ToList();
        ServiceCount = services.Count;
        AverageDuration = services.Count == 0 ? 0 : (int)Math.Round(services.Average(s => s.Sum(e => e.Block.ActualSeconds)));
        AverageOvertimePerService = services.Count == 0 ? 0 : (int)Math.Round(services.Average(s => Math.Max(0, s.Sum(e => e.Block.ActualSeconds - e.Block.PlannedSeconds))));
        OverBlocks = (Entries.Count(e => e.Block.IsOver), Entries.Count);

        ByPerson = Entries
            .Where(e => e.Block.PersonId is not null)
            .GroupBy(e => e.Block.PersonId!.Value)
            .Select(g =>
            {
                var over = g.Where(e => e.Block.IsOver).Select(e => e.Block.Overtime).ToList();
                return new PersonStats(
                    g.Key,
                    g.Select(e => e.Block.PersonName).LastOrDefault(n => n is not null),
                    g.Count(),
                    over.Count,
                    over.Count == 0 ? 0 : (int)Math.Round(over.Average()),
                    over.Count == 0 ? 0 : over.Max(),
                    over.Sum());
            })
            .OrderByDescending(p => p.TotalOvertime)
            .ThenByDescending(p => p.Participations)
            .ToList();

        ByBlock = Entries
            .GroupBy(e => NameKey.For(e.Block.Name))
            .Select(g =>
            {
                var over = g.Where(e => e.Block.IsOver).Select(e => e.Block.Overtime).ToList();
                return new BlockStats(
                    g.OrderByDescending(e => e.Record.Date).First().Block.Name,
                    over.Count,
                    g.Count(),
                    over.Count == 0 ? 0 : (int)Math.Round(over.Average()),
                    (int)Math.Round(g.Average(e => e.Block.ActualSeconds)),
                    (int)Math.Round(g.Average(e => e.Block.PlannedSeconds)));
            })
            .OrderByDescending(b => b.Total)
            .ThenBy(b => b.Name, StringComparer.Create(Spanish.Culture, ignoreCase: true))
            .ToList();
    }

    public IReadOnlyList<StatEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    public int ServiceCount { get; }

    public int AverageDuration { get; }

    public int AverageOvertimePerService { get; }

    public (int Over, int Total) OverBlocks { get; }

    public IReadOnlyList<PersonStats> ByPerson { get; }

    public IReadOnlyList<BlockStats> ByBlock { get; }

    /// <summary>The person's counted blocks in the period (most recent first).</summary>
    public IReadOnlyList<StatEntry> BlocksOf(Guid personId) => Entries
        .Where(e => e.Block.PersonId == personId)
        .OrderByDescending(e => e.Record.Date)
        .ToList();

    /// <summary>Distinct block names across all records (for the block filter menu).</summary>
    public static IReadOnlyList<string> BlockNames(IEnumerable<ServiceRecord> records) => records
        .OrderByDescending(r => r.Date)
        .SelectMany(r => r.Blocks)
        .GroupBy(b => NameKey.For(b.Name))
        .Select(g => g.First().Name)
        .OrderBy(n => n, StringComparer.Create(Spanish.Culture, ignoreCase: true))
        .ToList();
}
