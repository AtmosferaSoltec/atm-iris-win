using System;

namespace Iris.Core.Models;

/// <summary>
/// "Now", "today" and calendar periods in the church's time zone (api-contract §2). Everything on the wire is UTC;
/// everything shown to people goes through here. Local values use <see cref="DateTimeKind.Unspecified"/>.
/// </summary>
public sealed class ChurchClock
{
    private readonly Func<TimeZoneInfo> _zone;
    private readonly Func<DateTimeOffset> _utcNow;

    public ChurchClock(Func<TimeZoneInfo> zone, Func<DateTimeOffset>? utcNow = null)
    {
        _zone = zone;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ChurchClock(TimeZoneInfo zone, Func<DateTimeOffset>? utcNow = null)
        : this(() => zone, utcNow)
    {
    }

    public TimeZoneInfo Zone => _zone();

    /// <summary>Current wall-clock time at the church.</summary>
    public DateTime Now => ToLocal(_utcNow());

    public DateTime Today => Now.Date;

    public DateTime ToLocal(DateTimeOffset instant) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, Zone).DateTime, DateTimeKind.Unspecified);

    public DateTimeOffset ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var zone = Zone;
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    /// <summary>First day (00:00) of the month containing <paramref name="local"/>.</summary>
    public static DateTime MonthStart(DateTime local) => new(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// Resolves an IANA (or Windows) id. .NET 8 on Windows maps IANA ids through ICU; if that is unavailable
    /// the fixed "America/Lima" offset (UTC−5, no DST) is used for the default zone, otherwise the PC's zone.
    /// </summary>
    public static TimeZoneInfo ResolveZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }

            if (id == "America/Lima" || id == "America/Bogota")
            {
                return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-5), id, id);
            }
        }

        return TimeZoneInfo.Local;
    }
}
