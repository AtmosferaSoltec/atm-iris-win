using Iris.Core.Models;
using Iris.Core.Timing;

namespace Iris.Tests;

public class BlockTimerTests
{
    private static readonly DateTime T0 = new(2026, 10, 4, 10, 0, 0);

    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Carlos = Guid.NewGuid();

    private static List<BlockTemplate> Template() =>
    [
        new(Guid.NewGuid(), "Bienvenida", 10, Carlos),
        new(Guid.NewGuid(), "Alabanzas", 15, Ana),
        new(Guid.NewGuid(), "Prédica", 40, null),
    ];

    [Theory]
    [InlineData(0, 600, ClockState.Normal)]
    [InlineData(539, 600, ClockState.Normal)]
    [InlineData(540, 600, ClockState.Warning)]
    [InlineData(600, 600, ClockState.Warning)]
    [InlineData(601, 600, ClockState.Over)]
    public void ClockState_has_no_tolerance(int elapsed, int planned, ClockState expected) =>
        Assert.Equal(expected, BlockTimer.StateFor(elapsed, planned));

    [Fact]
    public void Start_runs_first_block_with_leader()
    {
        var timer = new BlockTimer(Template());
        timer.Start(Ana, T0);

        Assert.Equal(TimerPhase.Running, timer.Phase);
        Assert.Equal("Bienvenida", timer.Current!.Name);
        Assert.Equal(Ana, timer.Current.PersonId);
        Assert.Equal(T0, timer.StartedAt);
    }

    [Fact]
    public void Advance_closes_current_and_starts_next()
    {
        var timer = new BlockTimer(Template());
        timer.Start(null, T0);
        timer.Advance(Carlos, T0.AddMinutes(9));

        Assert.True(timer.Blocks[0].IsDone);
        Assert.Equal("Alabanzas", timer.Current!.Name);
        Assert.Equal(Carlos, timer.Current.PersonId);
        Assert.Equal(540, timer.Elapsed(timer.Blocks[0].Id, T0.AddHours(1)));
    }

    [Fact]
    public void Advance_on_last_block_finishes()
    {
        var timer = new BlockTimer(Template());
        timer.Start(null, T0);
        timer.Advance(null, T0.AddMinutes(10));
        timer.Advance(null, T0.AddMinutes(25));
        Assert.True(timer.IsOnLastBlock);

        timer.Advance(null, T0.AddMinutes(60));

        Assert.Equal(TimerPhase.Finished, timer.Phase);
        Assert.All(timer.Blocks, b => Assert.True(b.IsDone));
    }

    [Fact]
    public void SkipNext_skips_and_Advance_jumps_over_it()
    {
        var timer = new BlockTimer(Template());
        timer.Start(null, T0);
        timer.SkipNext();
        timer.Advance(null, T0.AddMinutes(10));

        Assert.Equal("Prédica", timer.Current!.Name);
        Assert.True(timer.Blocks[1].IsSkipped);
        Assert.Equal((10 + 40) * 60, timer.PlannedSeconds);
    }

    [Fact]
    public void AddBlock_goes_right_after_the_current_one()
    {
        var timer = new BlockTimer(Template());
        timer.Start(null, T0);
        var added = timer.AddBlock("Santa Cena", 20, Ana);

        Assert.Equal(1, timer.Blocks.ToList().FindIndex(b => b.Id == added.Id));
        Assert.True(added.IsAddedToday);
        Assert.Equal(1200, added.PlannedSeconds);
    }

    [Fact]
    public void Running_blocks_cannot_be_edited_but_their_leader_can()
    {
        var timer = new BlockTimer(Template());
        timer.Start(null, T0);
        var id = timer.Current!.Id;

        timer.Rename(id, "Otro");
        timer.SetMinutes(id, 99);
        timer.SetSkipped(id, true);
        timer.SetPerson(id, Ana);

        Assert.Equal("Bienvenida", timer.Current!.Name);
        Assert.Equal(600, timer.Current.PlannedSeconds);
        Assert.False(timer.Current.IsSkipped);
        Assert.Equal(Ana, timer.Current.PersonId);
    }

    [Fact]
    public void Changes_detect_added_skipped_edited_and_reordered_but_not_leader()
    {
        var timer = new BlockTimer(Template());
        Assert.False(timer.HasTemplateChanges);

        timer.SetPerson(timer.Blocks[0].Id, Ana);
        Assert.False(timer.HasTemplateChanges);

        timer.SetSkipped(timer.Blocks[2].Id, true);
        timer.SetMinutes(timer.Blocks[1].Id, 20);
        timer.AddBlock("Santa Cena", 20, null);

        var changes = timer.Changes;
        Assert.Contains("Santa Cena", changes.Added);
        Assert.Contains("Prédica", changes.Skipped);
        Assert.Contains("Alabanzas", changes.Edited);
        Assert.False(changes.IsReordered);

        var reordered = new BlockTimer(Template());
        reordered.MovePending(0, 1);
        Assert.True(reordered.Changes.IsReordered);
    }

    [Fact]
    public void Finishing_early_saves_unreached_blocks_as_skipped_without_template_change()
    {
        var timer = new BlockTimer(Template());
        timer.Start(Ana, T0);
        timer.Finish(T0.AddMinutes(12));

        var record = timer.Record(Guid.NewGuid(), T0, new Dictionary<Guid, string> { [Ana] = "Ana Torres" });

        Assert.False(timer.HasTemplateChanges);
        Assert.Equal(BlockStatus.Completed, record.Blocks[0].Status);
        Assert.Equal(720, record.Blocks[0].ActualSeconds);
        Assert.Equal("Ana Torres", record.Blocks[0].PersonName);
        Assert.All(record.Blocks.Skip(1), b =>
        {
            Assert.Equal(BlockStatus.Skipped, b.Status);
            Assert.Equal(0, b.ActualSeconds);
            Assert.Null(b.PersonId);
        });
        Assert.Equal(T0, record.Date);
    }

    [Fact]
    public void UpdatedTemplate_follows_today_without_skipped_and_assigns_leader_to_new_blocks()
    {
        var original = Template();
        var timer = new BlockTimer(original);
        timer.SetSkipped(timer.Blocks[2].Id, true);
        timer.SetMinutes(timer.Blocks[0].Id, 15);
        timer.Start(Ana, T0);
        var added = timer.AddBlock("Santa Cena", 20, Ana);

        var updated = timer.UpdatedTemplate(original);

        Assert.Equal(["Bienvenida", "Santa Cena", "Alabanzas"], updated.Select(t => t.Name));
        Assert.Equal(15, updated[0].PlannedMinutes);
        Assert.Equal(Carlos, updated[0].DefaultPersonId); // keeps the template's suggested leader
        Assert.Equal(Ana, updated.Single(t => t.Id == added.Id).DefaultPersonId);
    }

    [Fact]
    public void ReorderPending_applies_the_given_order()
    {
        var timer = new BlockTimer(Template());
        var ids = timer.Blocks.Select(b => b.Id).ToList();

        timer.ReorderPending([ids[2], ids[0], ids[1]]);

        Assert.Equal([ids[2], ids[0], ids[1]], timer.Blocks.Select(b => b.Id));
    }
}
