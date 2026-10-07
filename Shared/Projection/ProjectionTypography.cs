using System;
using System.Collections.Generic;
using Iris.Core.Models;
using Microsoft.UI.Xaml.Media;

namespace Iris.Shared.Projection;

/// <summary>
/// Translates a <see cref="ProjectionFontFamily"/> key into a real Windows font (api-contract §6; the
/// API keeps the suggested table in <c>src/modules/church/projection-fonts.ts</c>). The key is what
/// travels and syncs, never a font name; each name falls back to Segoe UI when it is missing.
/// </summary>
public static class ProjectionFonts
{
    private static readonly Dictionary<ProjectionFontFamily, (string Font, string Name)> Table = new()
    {
        [ProjectionFontFamily.System] = ("Segoe UI Variable Display, Segoe UI", "Segoe UI Variable (recomendada)"),
        [ProjectionFontFamily.SystemRounded] = ("Segoe UI", "Segoe UI"),
        [ProjectionFontFamily.Serif] = ("Georgia", "Georgia (serif)"),
        [ProjectionFontFamily.Georgia] = ("Georgia", "Georgia"),
        [ProjectionFontFamily.AvenirNext] = ("Century Gothic, Segoe UI", "Century Gothic"),
        [ProjectionFontFamily.Futura] = ("Bahnschrift, Segoe UI", "Bahnschrift"),
        [ProjectionFontFamily.GillSans] = ("Corbel, Segoe UI", "Corbel"),
        [ProjectionFontFamily.Optima] = ("Candara, Segoe UI", "Candara"),
        [ProjectionFontFamily.Baskerville] = ("Cambria, Georgia", "Cambria"),
        [ProjectionFontFamily.Palatino] = ("Palatino Linotype, Georgia", "Palatino Linotype"),
    };

    private static readonly Dictionary<ProjectionFontFamily, FontFamily> Cache = [];

    public static FontFamily FontFamily(ProjectionFontFamily family)
    {
        if (!Cache.TryGetValue(family, out var font))
        {
            font = new FontFamily(Table[family].Font);
            Cache[family] = font;
        }

        return font;
    }

    /// <summary>Shown in Proyección; "(recomendada)" only on the default.</summary>
    public static string DisplayName(ProjectionFontFamily family) => Table[family].Name;
}

/// <summary>
/// How the projected lyrics look right now (api-contract §6), the same for every surface of this window and the TV
/// window: thumbnails, EN VIVO and the TV read it, so they keep looking identical. The console and Proyección set it.
/// </summary>
public static class ProjectionTypography
{
    public static ProjectionSettings Current { get; private set; } = ProjectionSettings.Default;

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes; every canvas redraws.</summary>
    public static event EventHandler? Changed;

    public static void Set(ProjectionSettings settings)
    {
        if (Current == settings)
        {
            return;
        }

        Current = settings;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
