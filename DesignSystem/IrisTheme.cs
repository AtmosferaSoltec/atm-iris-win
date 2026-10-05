using System;
using System.Collections.Generic;
using System.Globalization;
using Iris.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Iris.DesignSystem;

/// <summary>Typed access to Tokens.xaml plus brush builders for data-driven colors (backgrounds, artwork).</summary>
public static class IrisTheme
{
    public static Color Color(string key) => (Color)Application.Current.Resources[key];

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static double Double(string key) => (double)Application.Current.Resources[key];

    public static Color WithOpacity(Color color, double opacity) =>
        Windows.UI.Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

    public static SolidColorBrush Tint(Color color, double opacity) => new(WithOpacity(color, opacity));

    public static Color ParseHex(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 6)
        {
            value = "FF" + value;
        }

        var argb = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
    }

    /// <summary>Diagonal gradient through <paramref name="hexColors"/> (backgrounds, image artwork).</summary>
    public static LinearGradientBrush DiagonalGradient(IReadOnlyList<string> hexColors)
    {
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        for (var i = 0; i < hexColors.Count; i++)
        {
            brush.GradientStops.Add(new GradientStop
            {
                Color = ParseHex(hexColors[i]),
                Offset = hexColors.Count == 1 ? 0 : (double)i / (hexColors.Count - 1),
            });
        }

        return brush;
    }

    /// <summary>x:Bind helper for data colors (service type colors).</summary>
    public static Brush HexBrush(string hex) => new SolidColorBrush(ParseHex(hex));

    /// <summary>x:Bind helper: a data color at <paramref name="opacity"/>.</summary>
    public static Brush HexTint(string hex, double opacity) => Tint(ParseHex(hex), opacity);

    /// <summary>x:Bind helper: warm-white capsule when selected, glass otherwise (pills, segments).</summary>
    public static Brush PillBackground(bool isSelected) => Brush(isSelected ? "IrisTextPrimaryBrush" : "IrisGlassBrush");

    public static Brush PillForeground(bool isSelected) => Brush(isSelected ? "IrisTextInverseBrush" : "IrisTextPrimaryBrush");

    /// <summary>x:Bind helper: success when on, tertiary when off (module status dots).</summary>
    public static Brush StatusBrush(bool isOn) => Brush(isOn ? "IrisSuccessBrush" : "IrisTextTertiaryBrush");

    /// <summary>x:Bind helper for swatches.</summary>
    public static Brush BackgroundBrush(ProjectionBackground? background)
    {
        if (background is null)
        {
            return Brush("IrisBlackBrush");
        }

        // A church picture shows as a small decoded copy; unreadable files fall back to the gradient.
        if (background.ImagePath is { } path && System.IO.File.Exists(path))
        {
            return new Microsoft.UI.Xaml.Media.ImageBrush
            {
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 240, UriSource = new Uri(path) },
            };
        }

        return DiagonalGradient(background.Colors);
    }

    /// <summary>x:Bind helper for the sync chip: warning offline, danger on failure, secondary otherwise.</summary>
    public static Brush SyncToneBrush(bool isOffline, bool isFailed) =>
        Brush(isFailed ? "IrisDangerBrush" : isOffline ? "IrisWarningBrush" : "IrisTextSecondaryBrush");

    /// <summary>x:Bind helper: TV connected (success) or missing (warning).</summary>
    public static Brush DisplayStatusBrush(bool isConnected) => Brush(isConnected ? "IrisSuccessBrush" : "IrisWarningBrush");

    /// <summary>x:Bind helper for library thumbnails.</summary>
    public static Brush ArtworkBrush(IReadOnlyList<string> artwork) => DiagonalGradient(artwork);

    /// <summary>Radial glow used over backgrounds and artwork.</summary>
    public static RadialGradientBrush Glow(Color color, double opacity, Windows.Foundation.Point center, double radiusX, double radiusY) => new()
    {
        Center = center,
        GradientOrigin = center,
        RadiusX = radiusX,
        RadiusY = radiusY,
        MappingMode = BrushMappingMode.RelativeToBoundingBox,
        GradientStops =
        {
            new GradientStop { Color = WithOpacity(color, opacity), Offset = 0 },
            new GradientStop { Color = WithOpacity(color, 0), Offset = 1 },
        },
    };
}

/// <summary>Per-kind name, glyph and color (IRIS_SPEC §9).</summary>
public static class KindStyle
{
    public static string Name(ServiceItemKind kind) => kind switch
    {
        ServiceItemKind.Song => "Letra",
        ServiceItemKind.Scripture => "Pasaje",
        ServiceItemKind.Announcement => "Anuncio",
        ServiceItemKind.Music => "Música",
        ServiceItemKind.Image => "Imagen",
        _ => "Video",
    };

    public static string Glyph(ServiceItemKind kind) => kind switch
    {
        ServiceItemKind.Song => Glyphs.Quote,
        ServiceItemKind.Scripture => Glyphs.Book,
        ServiceItemKind.Announcement => Glyphs.Megaphone,
        ServiceItemKind.Music => Glyphs.MusicNote,
        ServiceItemKind.Image => Glyphs.Photo,
        _ => Glyphs.Video,
    };

    public static Color Color(ServiceItemKind kind) => IrisTheme.Color(kind switch
    {
        ServiceItemKind.Song => "IrisEmberColor",
        ServiceItemKind.Scripture => "IrisVioletColor",
        ServiceItemKind.Announcement => "IrisIndigoColor",
        ServiceItemKind.Music => "IrisSuccessColor",
        ServiceItemKind.Image => "IrisCoralColor",
        _ => "IrisRoseColor",
    });

    public static Brush Brush(ServiceItemKind kind) => new SolidColorBrush(Color(kind));

    /// <summary>Tinted fill (14%) behind kind icons.</summary>
    public static Brush TintBrush(ServiceItemKind kind) => IrisTheme.Tint(Color(kind), 0.14);
}

/// <summary>Segoe Fluent Icons code points (SF Symbol equivalents).</summary>
public static class Glyphs
{
    public const string Quote = "";
    public const string Book = "";
    public const string Megaphone = "";
    public const string MusicNote = "";
    public const string Photo = "";
    public const string Video = "";
    public const string Waveform = "";
    public const string Tv = "";
    public const string Building = "";
    public const string Person = "";
    public const string Mail = "";
    public const string MailOpen = "";
    public const string Lock = "";
    public const string Key = "";
    public const string Eye = "";
    public const string EyeSlash = "";
    public const string ErrorCircle = "";
    public const string Warning = "";
    public const string Success = "";
    public const string Info = "";
    public const string CheckMark = "";
    public const string Close = "";
    public const string Add = "";
    public const string More = "";
    public const string Delete = "";
    public const string Copy = "";
    public const string Up = "";
    public const string Down = "";
    public const string Eraser = "";
    public const string Background = "";
    public const string Edit = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";
    public const string Play = "";
    public const string Pause = "";
    public const string Stop = "";
    public const string Previous = "";
    public const string Repeat = "";
    public const string Search = "";
    public const string SignOut = "";
    public const string Timer = "";
    public const string Calendar = "";
    public const string People = "";
    public const string Modules = "";
    public const string Library = "";
    public const string Clock = "";
}

/// <summary>Spectrum rotation for avatars, timeline blocks and summary rows (<c>spectrum[i % 5]</c>).</summary>
public static class Spectrum
{
    private static readonly string[] Keys = ["IrisEmberColor", "IrisCoralColor", "IrisRoseColor", "IrisVioletColor", "IrisIndigoColor"];

    public static Color Color(int index) => IrisTheme.Color(Keys[((index % Keys.Length) + Keys.Length) % Keys.Length]);

    public static Brush Brush(int index) => new SolidColorBrush(Color(index));
}
