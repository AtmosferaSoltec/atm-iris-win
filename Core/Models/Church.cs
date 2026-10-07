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

    /// <summary>What the church actually sees: its own choice, minus whatever Iris has switched off
    /// for every church (api-contract §6).</summary>
    public ChurchModules Effective(ChurchModules available) => new(
        Bible && available.Bible,
        Multimedia && available.Multimedia,
        TimeControl && available.TimeControl);
}

/// <summary>
/// One of the 10 typefaces Iris offers for the projected lyrics (api-contract §6). The real font
/// behind each key is a platform concern (<c>Shared/Projection/ProjectionFonts.cs</c>); this is the
/// key that travels over the wire and syncs, never a font name.
/// </summary>
public enum ProjectionFontFamily
{
    System,
    SystemRounded,
    Serif,
    Georgia,
    AvenirNext,
    Futura,
    GillSans,
    Optima,
    Baskerville,
    Palatino,
}

/// <summary>
/// How the projected lyrics look, same for every console of the church (api-contract §6).
/// <see cref="FontSizePt"/> is measured on a 1920-wide screen; every surface scales it by
/// <c>actualWidth / 1920</c>. <see cref="DefaultBackgroundId"/> is a gradient key or a media asset
/// id shown while nothing is chosen; null is plain black. Not validated against anything: a
/// dangling id just falls back to black.
/// </summary>
public sealed record ProjectionSettings(ProjectionFontFamily FontFamily, int FontSizePt, string? DefaultBackgroundId)
{
    public static readonly ProjectionSettings Default = new(ProjectionFontFamily.System, 88, null);

    public static readonly IReadOnlyList<int> SuggestedFontSizes = [56, 64, 72, 80, 88, 96, 112, 128, 144];

    public const int MinFontSizePt = 40;
    public const int MaxFontSizePt = 200;
}

/// <summary>Fixed weekly schedule. <see cref="Weekday"/>: 1 = Sunday … 7 = Saturday.</summary>
public sealed record ServiceSchedule(int Weekday, int Hour, int Minute)
{
    public DayOfWeek DayOfWeek => (DayOfWeek)(Weekday - 1);
}

// api-contract §9: no responsible person on the template — it rotates weekly and is recorded on
// each service instead.
public sealed record BlockTemplate(Guid Id, string Name, int PlannedMinutes);

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
