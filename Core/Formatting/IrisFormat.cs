using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Iris.Core.Formatting;

/// <summary>Dates and times are always Spanish, whatever the system language (IRIS_SPEC §13).</summary>
public static class Spanish
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-ES");

    private static readonly string[] WeekdayNames = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];
    private static readonly string[] WeekdayShort = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"];
    private static readonly string[] MonthNames = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
    private static readonly string[] MonthShort = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sept", "oct", "nov", "dic"];

    public static string Weekday(DayOfWeek day) => WeekdayNames[(int)day];

    /// <summary>"Dom", "Lun"… for the schedule pills.</summary>
    public static string WeekdayPill(DayOfWeek day) => Capitalize(WeekdayShort[(int)day]);

    public static string Month(int month) => MonthNames[month - 1];

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], Culture) + text[1..];

    /// <summary>"domingo, 27 de septiembre de 2026".</summary>
    public static string LongDate(DateTime date) => $"{Weekday(date.DayOfWeek)}, {date.Day} de {Month(date.Month)} de {date.Year}";

    /// <summary>"domingo, 4 de octubre".</summary>
    public static string DayAndMonth(DateTime date) => $"{Weekday(date.DayOfWeek)}, {date.Day} de {Month(date.Month)}";

    /// <summary>"dom, 27 sept".</summary>
    public static string ShortDate(DateTime date) => $"{WeekdayShort[(int)date.DayOfWeek]}, {date.Day} {MonthShort[date.Month - 1]}";

    /// <summary>"septiembre 2026".</summary>
    public static string MonthYear(int year, int month) => $"{Month(month)} {year}";

    public static string Time(DateTime time) => time.ToString("HH:mm", Culture);

    public static string Time(int hour, int minute) => $"{hour:00}:{minute:00}";
}

/// <summary>Planned/actual durations and deltas.</summary>
public static class IrisDurationFormat
{
    /// <summary>"1 h y 10 min", "40 min", "2 h".</summary>
    public static string Planned(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours == 0 ? $"{rest} min" : rest == 0 ? $"{hours} h" : $"{hours} h y {rest} min";
    }

    /// <summary>"9:40", "51:30", "1:26:10" (whole seconds).</summary>
    public static string Clock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>"+3:10" for overtime, "−21:18" for time left.</summary>
    public static string Delta(int seconds) => (seconds < 0 ? "−" : "+") + Clock(Math.Abs(seconds));

    /// <summary>"4 bloques · 1 h y 10 min" / "1 bloque · 10 min".</summary>
    public static string BlocksSummary(int count, int minutes) => $"{Plural.Count(count, "bloque", "bloques")} · {Planned(minutes)}";
}

public static class Plural
{
    /// <summary>"1 bloque", "4 bloques".</summary>
    public static string Count(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";
}

/// <summary>Comparison key: no accents, case or surrounding spaces ("  José " == "jose").</summary>
public static class NameKey
{
    public static string For(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class PersonInitials
{
    public static string From(string name) => string.Concat(name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(w => char.ToUpper(w[0], Spanish.Culture)));
}
