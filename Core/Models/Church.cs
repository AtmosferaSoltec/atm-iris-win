using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Formatting;

namespace Iris.Core.Models;

/// <summary>Someone who leads service blocks (welcome, worship, sermon…).</summary>
public sealed record Person(Guid Id, string Name, int BlockCount = 0)
{
    /// <summary>"Daniel Ruiz" → "DR"; one word → its first letter.</summary>
    public string Initials => PersonInitials.From(Name);
}

/// <summary>Per-church modules (IRIS_SPEC §6.6). Lyrics are always on.</summary>
public sealed record ChurchModules(bool Bible, bool Multimedia, bool TimeControl)
{
    public static readonly ChurchModules All = new(true, true, true);
}

/// <summary>Fixed weekly schedule. <see cref="Weekday"/>: 1 = Sunday … 7 = Saturday.</summary>
public sealed record ServiceSchedule(int Weekday, int Hour, int Minute)
{
    public DayOfWeek DayOfWeek => (DayOfWeek)(Weekday - 1);
}

public sealed record BlockTemplate(Guid Id, string Name, int PlannedMinutes, Guid? DefaultPersonId);

/// <summary>A kind of service ("Culto general") with an optional schedule and time blocks.</summary>
public sealed record ServiceType(Guid Id, string Name, string Color, ServiceSchedule? Schedule, IReadOnlyList<BlockTemplate> Blocks)
{
    public bool TracksTime => Blocks.Count > 0;

    public int PlannedMinutes => Blocks.Sum(b => b.PlannedMinutes);
}

/// <summary>The six service colors (IRIS_SPEC §9).</summary>
public static class ServicePalette
{
    public static IReadOnlyList<(string Hex, string Name)> Colors { get; } =
    [
        ("#FFB547", "Ámbar"),
        ("#FF7A59", "Coral"),
        ("#F0508C", "Rosa"),
        ("#9B5CFF", "Violeta"),
        ("#4E5BFF", "Índigo"),
        ("#3DDC97", "Verde"),
    ];
}

public enum BlockStatus
{
    Completed,
    Skipped,
    Adjusted,
}

/// <summary>One block of a saved service. Only times are stored, never the content used.</summary>
public sealed record BlockRecord(Guid Id, string Name, int PlannedSeconds, int ActualSeconds, Guid? PersonId, string? PersonName, BlockStatus Status)
{
    /// <summary>Skipped blocks never count in any calculation; adjusted ones do.</summary>
    public bool IsCounted => Status != BlockStatus.Skipped;

    /// <summary>Actual − planned when positive, with no tolerance.</summary>
    public int Overtime => IsCounted ? Math.Max(0, ActualSeconds - PlannedSeconds) : 0;

    public bool IsOver => Overtime > 0;
}

public sealed record ServiceRecord(Guid Id, DateTime Date, Guid ServiceTypeId, IReadOnlyList<BlockRecord> Blocks, string ServiceTypeName = "")
{
    public int PlannedSeconds => Blocks.Where(b => b.IsCounted).Sum(b => b.PlannedSeconds);

    public int ActualSeconds => Blocks.Where(b => b.IsCounted).Sum(b => b.ActualSeconds);

    public int OvertimeSeconds => Math.Max(0, ActualSeconds - PlannedSeconds);
}
