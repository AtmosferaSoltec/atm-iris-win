using System;
using Iris.Core.Sync;

namespace Iris.Core.Formatting;

/// <summary>Spanish texts of the sync indicator.</summary>
public static class SyncText
{
    /// <summary>"ahora", "hace 2 min", "hace 3 h", "hace 2 d".</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        return span.TotalMinutes < 1 ? "ahora"
            : span.TotalMinutes < 60 ? $"hace {(int)span.TotalMinutes} min"
            : span.TotalHours < 24 ? $"hace {(int)span.TotalHours} h"
            : $"hace {(int)span.TotalDays} d";
    }

    /// <summary>"1 cambio pendiente", "3 cambios pendientes".</summary>
    public static string Pending(int count) => count == 1 ? "1 cambio pendiente" : $"{count} cambios pendientes";

    /// <summary>The one-line state shown in the top bar.</summary>
    public static string Headline(SyncStatus status, DateTimeOffset now) => status.Phase switch
    {
        SyncPhase.Syncing => status.IsFirstSync ? "Descargando tu iglesia…" : "Actualizando…",
        SyncPhase.Offline => status.Pending > 0 ? $"Sin conexión · {Pending(status.Pending)}" : "Sin conexión",
        SyncPhase.Failed => "No se pudo actualizar",
        _ when status.Pending > 0 => Pending(status.Pending),
        _ when status.LastSync is { } last => $"Actualizado {Ago(last, now)}",
        _ => "Sin actualizar",
    };
}
