using Iris.Core.Models;
using Iris.Core.Timing;

namespace Iris.Tests;

public class TimeStatisticsTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0);
    private static readonly Guid Type = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Carlos = Guid.NewGuid();

    private static BlockRecord Block(string name, int planned, int actual, Guid? person, BlockStatus status = BlockStatus.Completed) =>
        new(Guid.NewGuid(), name, planned, actual, person, person is null ? null : "Persona", status);

    private static ServiceRecord Service(DateTime date, params BlockRecord[] blocks) => new(Guid.NewGuid(), date, Type, blocks);

    [Fact]
    public void Overtime_has_no_margin_and_skipped_blocks_do_not_count()
    {
        var record = Service(
            Now.AddDays(-7),
            Block("Prédica", 600, 601, Ana),
            Block("Anuncios", 300, 100, Carlos),
            Block("Santa Cena", 300, 999, null, BlockStatus.Skipped));

        Assert.Equal(900, record.PlannedSeconds);
        Assert.Equal(701, record.ActualSeconds);
        Assert.Equal(0, record.OvertimeSeconds); // 701 < 900: the service is on time even if one block ran over
        Assert.Equal(1, record.Blocks[0].Overtime);
        Assert.Equal(0, record.Blocks[2].Overtime);
    }

    [Fact]
    public void Adjusted_blocks_count()
    {
        var stats = new TimeStatistics(
            [Service(Now, Block("Prédica", 600, 900, Ana, BlockStatus.Adjusted))],
            SummaryFilter.Default,
            Now);

        Assert.Equal((1, 1), stats.OverBlocks);
        Assert.Equal(300, stats.AverageOvertimePerService);
    }

    [Fact]
    public void Periods_are_calendar_months()
    {
        var thisMonth = new SummaryPeriod(PeriodKind.ThisMonth);
        var last = new SummaryPeriod(PeriodKind.LastMonth);
        var three = new SummaryPeriod(PeriodKind.Last3Months);

        Assert.True(thisMonth.Contains(new DateTime(2026, 10, 1), Now));
        Assert.False(thisMonth.Contains(new DateTime(2026, 9, 30), Now));
        Assert.True(last.Contains(new DateTime(2026, 9, 30), Now));
        Assert.True(three.Contains(new DateTime(2026, 8, 1), Now));
        Assert.False(three.Contains(new DateTime(2026, 7, 31), Now));
        Assert.True(new SummaryPeriod(PeriodKind.All).Contains(new DateTime(2001, 1, 1), Now));
        Assert.True(new SummaryPeriod(PeriodKind.Month, 2025, 12).Contains(new DateTime(2025, 12, 31), Now));
    }

    [Fact]
    public void Service_counts_when_one_block_passes_the_filter_and_averages_are_per_service()
    {
        var records = new[]
        {
            Service(Now.AddDays(-7), Block("Prédica", 600, 900, Ana), Block("Anuncios", 300, 300, Carlos)),
            Service(Now.AddDays(-14), Block("Prédica", 600, 500, Ana)),
        };

        var all = new TimeStatistics(records, SummaryFilter.Default, Now);
        Assert.Equal(2, all.ServiceCount);
        Assert.Equal(850, all.AverageDuration); // (1200 + 500) / 2
        Assert.Equal(150, all.AverageOvertimePerService); // (300 + 0) / 2

        var byBlock = new TimeStatistics(records, SummaryFilter.Default with { BlockName = "  prédica " }, Now);
        Assert.Equal(2, byBlock.ServiceCount);
        Assert.Equal(700, byBlock.AverageDuration);
    }

    [Fact]
    public void ByPerson_orders_by_total_overtime_and_averages_only_overtime_occasions()
    {
        var records = new[]
        {
            Service(Now.AddDays(-7), Block("Prédica", 600, 900, Ana), Block("Anuncios", 300, 200, Carlos)),
            Service(Now.AddDays(-14), Block("Prédica", 600, 700, Ana), Block("Anuncios", 300, 100, Ana)),
        };

        var stats = new TimeStatistics(records, SummaryFilter.Default, Now);

        var ana = stats.ByPerson[0];
        Assert.Equal(Ana, ana.PersonId);
        Assert.Equal(3, ana.Participations);
        Assert.Equal(2, ana.TimesOver);
        Assert.Equal(200, ana.AverageOvertimeWhenOver); // (300 + 100) / 2, the on-time block is ignored
        Assert.Equal(300, ana.MaxOvertime);
        Assert.Equal(400, ana.TotalOvertime);
        Assert.Equal(Carlos, stats.ByPerson[1].PersonId);
        Assert.Equal(0, stats.ByPerson[1].TotalOvertime);
    }

    [Fact]
    public void ByBlock_groups_without_accents_or_case()
    {
        var records = new[]
        {
            Service(Now.AddDays(-7), Block("Prédica", 600, 900, Ana)),
            Service(Now.AddDays(-14), Block("PREDICA", 600, 400, Ana)),
        };

        var stats = new TimeStatistics(records, SummaryFilter.Default, Now);

        var block = Assert.Single(stats.ByBlock);
        Assert.Equal(2, block.Total);
        Assert.Equal(1, block.TimesOver);
        Assert.Equal(650, block.AverageActual);
        Assert.Equal(600, block.AveragePlanned);
    }

    [Fact]
    public void Empty_when_filters_match_nothing()
    {
        var stats = new TimeStatistics(
            [Service(Now, Block("Prédica", 600, 900, Ana))],
            SummaryFilter.Default with { PersonId = Guid.NewGuid() },
            Now);

        Assert.True(stats.IsEmpty);
        Assert.Equal(0, stats.ServiceCount);
        Assert.Equal(0, stats.AverageDuration);
    }

    [Fact]
    public void BlockNames_are_distinct_by_name_key()
    {
        var names = TimeStatistics.BlockNames(
        [
            Service(Now, Block("Prédica", 1, 1, null), Block("Alabanzas", 1, 1, null)),
            Service(Now.AddDays(-7), Block("predica", 1, 1, null)),
        ]);

        Assert.Equal(["Alabanzas", "Prédica"], names);
    }
}
