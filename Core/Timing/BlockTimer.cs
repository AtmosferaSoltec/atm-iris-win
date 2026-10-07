using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;

namespace Iris.Core.Timing;

public enum TimerPhase
{
    NotStarted,
    Running,
    Finished,
}

public enum ClockState
{
    Normal,
    Warning,
    Over,
}

/// <summary>
/// A block during today's service. <see cref="Id"/> is the template block's id, or a new one for
/// blocks added today. <see cref="OriginalName"/>/<see cref="OriginalPlannedSeconds"/> remember
/// the template values so edits can be detected.
/// </summary>
public sealed record TimerBlock(
    Guid Id,
    string Name,
    int PlannedSeconds,
    Guid? PersonId,
    DateTime? StartedAt = null,
    DateTime? EndedAt = null,
    bool IsSkipped = false,
    bool IsAddedToday = false,
    string? OriginalName = null,
    int OriginalPlannedSeconds = 0)
{
    public bool IsStarted => StartedAt is not null;

    public bool IsRunning => StartedAt is not null && EndedAt is null;

    public bool IsDone => EndedAt is not null;

    /// <summary>Not started yet: can still be edited, skipped or reordered.</summary>
    public bool IsPending => StartedAt is null;

    public bool IsEdited => !IsAddedToday && (Name != OriginalName || PlannedSeconds != OriginalPlannedSeconds);
}

/// <summary>What changed today compared to the template (IRIS_SPEC §7.9). Choosing a different leader is not a change.</summary>
public sealed record TemplateChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped, IReadOnlyList<string> Edited, bool IsReordered)
{
    public bool HasChanges => Added.Count > 0 || Skipped.Count > 0 || Edited.Count > 0 || IsReordered;
}

/// <summary>
/// Pure block-timer logic (IRIS_SPEC §6.9, §7.9). Time is always passed in, so it is testable.
/// State: <c>NotStarted → Running → Finished</c>. One block runs at a time; the previous one is
/// closed when the next one starts.
/// </summary>
public sealed class BlockTimer
{
    private readonly List<TimerBlock> _blocks;
    private readonly IReadOnlyList<Guid> _templateOrder;

    public BlockTimer(IEnumerable<BlockTemplate> template)
    {
        _blocks = template
            // No suggested leader anymore (api-contract §9): it rotates weekly, chosen fresh each time.
            .Select(t => new TimerBlock(t.Id, t.Name, t.PlannedMinutes * 60, PersonId: null, OriginalName: t.Name, OriginalPlannedSeconds: t.PlannedMinutes * 60))
            .ToList();
        _templateOrder = _blocks.Select(b => b.Id).ToList();
    }

    public IReadOnlyList<TimerBlock> Blocks => _blocks;

    public TimerPhase Phase { get; private set; } = TimerPhase.NotStarted;

    public int CurrentIndex => _blocks.FindIndex(b => b.IsRunning);

    public TimerBlock? Current => CurrentIndex is var i and >= 0 ? _blocks[i] : null;

    /// <summary>The next block that will run (pending and not skipped).</summary>
    public TimerBlock? Next => _blocks.Skip(Math.Max(0, CurrentIndex + 1)).FirstOrDefault(b => b.IsPending && !b.IsSkipped);

    public bool IsOnLastBlock => Phase == TimerPhase.Running && Next is null;

    /// <summary>Blocks that have not started (skipped ones included), in order.</summary>
    public IEnumerable<TimerBlock> Pending => _blocks.Where(b => b.IsPending);

    public int PlannedSeconds => _blocks.Where(b => !b.IsSkipped).Sum(b => b.PlannedSeconds);

    public DateTime? StartedAt => _blocks.Where(b => b.StartedAt is not null).Min(b => b.StartedAt);

    public TimerBlock Block(Guid id) => _blocks.First(b => b.Id == id);

    // ===== Flow =====

    /// <summary>Starts the first block that is not skipped with <paramref name="personId"/> as its leader.</summary>
    public void Start(Guid? personId, DateTime at)
    {
        if (Phase != TimerPhase.NotStarted)
        {
            return;
        }

        var first = _blocks.FindIndex(b => !b.IsSkipped);
        if (first < 0)
        {
            return;
        }

        _blocks[first] = _blocks[first] with { PersonId = personId, StartedAt = at };
        Phase = TimerPhase.Running;
    }

    /// <summary>Closes the current block and starts the next one; with no next block it finishes.</summary>
    public void Advance(Guid? nextPersonId, DateTime at)
    {
        if (Phase != TimerPhase.Running)
        {
            return;
        }

        if (Next is not { } next)
        {
            Finish(at);
            return;
        }

        CloseCurrent(at);
        var index = _blocks.IndexOf(next);
        _blocks[index] = next with { PersonId = nextPersonId, StartedAt = at };
    }

    /// <summary>Closes the current block. Blocks never reached are saved as skipped, but are not a template change.</summary>
    public void Finish(DateTime at)
    {
        if (Phase != TimerPhase.Running)
        {
            return;
        }

        CloseCurrent(at);
        Phase = TimerPhase.Finished;
    }

    private void CloseCurrent(DateTime at)
    {
        if (CurrentIndex is var i and >= 0)
        {
            _blocks[i] = _blocks[i] with { EndedAt = at };
        }
    }

    // ===== Today's changes =====

    /// <summary>Adds a block right after the current one (or at the start before beginning).</summary>
    public TimerBlock AddBlock(string name, int minutes, Guid? personId)
    {
        var block = new TimerBlock(Guid.NewGuid(), name.Trim(), Math.Clamp(minutes, 1, 240) * 60, personId, IsAddedToday: true);
        var after = CurrentIndex >= 0 ? CurrentIndex : _blocks.FindLastIndex(b => !b.IsPending);
        _blocks.Insert(after + 1, block);
        return block;
    }

    public void SkipNext()
    {
        if (Next is { } next)
        {
            SetSkipped(next.Id, true);
        }
    }

    public void SetSkipped(Guid id, bool isSkipped) => UpdateWhere(id, b => b.IsPending, b => b with { IsSkipped = isSkipped });

    public void Rename(Guid id, string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            UpdateWhere(id, b => b.IsPending, b => b with { Name = name.Trim() });
        }
    }

    public void SetMinutes(Guid id, int minutes) => UpdateWhere(id, b => b.IsPending, b => b with { PlannedSeconds = Math.Clamp(minutes, 1, 240) * 60 });

    /// <summary>Sets a block's leader (pending ones, or the running one to correct it).</summary>
    public void SetPerson(Guid id, Guid? personId) => UpdateWhere(id, b => !b.IsDone, b => b with { PersonId = personId });

    /// <summary>Moves a pending block to another pending position (indices within <see cref="Pending"/>).</summary>
    public void MovePending(int from, int to)
    {
        var pending = Pending.ToList();
        if (from < 0 || from >= pending.Count || to < 0 || to >= pending.Count || from == to)
        {
            return;
        }

        var firstPending = _blocks.IndexOf(pending[0]);
        var block = pending[from];
        _blocks.RemoveAt(firstPending + from);
        _blocks.Insert(firstPending + to, block);
    }

    /// <summary>Puts the pending blocks in <paramref name="order"/> (ids not listed keep their relative order at the end).</summary>
    public void ReorderPending(IReadOnlyList<Guid> order)
    {
        var pending = Pending.ToList();
        if (pending.Count < 2)
        {
            return;
        }

        var firstPending = _blocks.IndexOf(pending[0]);
        var rank = order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var sorted = pending.OrderBy(b => rank.GetValueOrDefault(b.Id, int.MaxValue)).ToList();
        _blocks.RemoveRange(firstPending, pending.Count);
        _blocks.InsertRange(firstPending, sorted);
    }

    private void UpdateWhere(Guid id, Func<TimerBlock, bool> allowed, Func<TimerBlock, TimerBlock> update)
    {
        var index = _blocks.FindIndex(b => b.Id == id);
        if (index >= 0 && allowed(_blocks[index]))
        {
            _blocks[index] = update(_blocks[index]);
        }
    }

    // ===== Clock =====

    public int Elapsed(Guid id, DateTime now)
    {
        var block = Block(id);
        return block.StartedAt is { } start ? (int)Math.Floor(((block.EndedAt ?? now) - start).TotalSeconds) : 0;
    }

    /// <summary>ratio = actual / planned: normal &lt; 0.9 · warning 0.9…1 · over &gt; 1 (no tolerance).</summary>
    public ClockState StateOf(Guid id, DateTime now) => StateFor(Elapsed(id, now), Block(id).PlannedSeconds);

    public static ClockState StateFor(int elapsedSeconds, int plannedSeconds)
    {
        if (elapsedSeconds > plannedSeconds)
        {
            return ClockState.Over;
        }

        return elapsedSeconds * 10 >= plannedSeconds * 9 ? ClockState.Warning : ClockState.Normal;
    }

    // ===== Template =====

    public TemplateChanges Changes
    {
        get
        {
            var added = _blocks.Where(b => b.IsAddedToday && !b.IsSkipped).Select(b => b.Name).ToList();
            var skipped = _blocks.Where(b => !b.IsAddedToday && b.IsSkipped).Select(b => b.Name).ToList();
            var edited = _blocks.Where(b => !b.IsSkipped && b.IsEdited).Select(b => b.OriginalName ?? b.Name).ToList();
            var todayOrder = _blocks.Where(b => !b.IsAddedToday && !b.IsSkipped).Select(b => b.Id).ToList();
            var templateOrder = _templateOrder.Where(todayOrder.Contains).ToList();
            return new TemplateChanges(added, skipped, edited, !todayOrder.SequenceEqual(templateOrder));
        }
    }

    public bool HasTemplateChanges => Changes.HasChanges;

    /// <summary>Saves only times: name, planned, actual, leader (id + name now) and status. Date = first block start.</summary>
    public ServiceRecord Record(Guid serviceTypeId, DateTime fallbackDate, IReadOnlyDictionary<Guid, string> peopleNames, string serviceTypeName = "") =>
        new(Guid.NewGuid(), StartedAt ?? fallbackDate, serviceTypeId, _blocks.Select(b =>
        {
            var ran = b.StartedAt is not null && !b.IsSkipped;
            var personId = ran ? b.PersonId : null;
            return new BlockRecord(
                b.Id,
                b.Name,
                b.PlannedSeconds,
                ran ? (int)Math.Floor(((b.EndedAt ?? b.StartedAt!.Value) - b.StartedAt!.Value).TotalSeconds) : 0,
                personId,
                personId is { } id && peopleNames.TryGetValue(id, out var name) ? name : null,
                ran ? BlockStatus.Completed : BlockStatus.Skipped);
        }).ToList(), serviceTypeName);

    /// <summary>"Guardar en la plantilla": today's order, names and minutes without the skipped ones.</summary>
    public IReadOnlyList<BlockTemplate> UpdatedTemplate() => _blocks
        .Where(b => !b.IsSkipped)
        .Select(b => new BlockTemplate(b.Id, b.Name, b.PlannedSeconds / 60))
        .ToList();
}
