using System;

namespace Iris.Core.Models;

/// <summary>
/// What a file needs to be a lyrics background (api-contract §11): an image or a video, 16:9, within
/// a size range, a video up to 30 s (it loops silently behind the lyrics, so a short clip is enough
/// and stays small). The API and the iPad app carry the same table; change a value in all three.
/// </summary>
public static class MediaBackgroundRules
{
    private const double AspectRatio = 16.0 / 9.0;
    private const double AspectTolerance = 0.02;

    public const long ImageMaxBytes = 10 * 1024 * 1024;
    public const long VideoMaxBytes = 100 * 1024 * 1024;
    public const int MinWidth = 1280;
    public const int ImageMaxWidth = 3840;
    public const int VideoMaxWidth = 1920;
    public const int VideoMaxSeconds = 30;

    /// <summary>Why a file cannot be a background, or null when it can.</summary>
    public static string? Problem(MediaKind kind, long sizeBytes, int? width, int? height, double? durationSeconds)
    {
        if (kind == MediaKind.Music)
        {
            return "Un audio no puede ser fondo. Usa una imagen o un video.";
        }

        var maxBytes = kind == MediaKind.Video ? VideoMaxBytes : ImageMaxBytes;
        if (sizeBytes > maxBytes)
        {
            return $"El archivo pesa demasiado para un fondo. El máximo es {maxBytes / 1024 / 1024} MB.";
        }

        if (width is not { } w || height is not { } h || w <= 0 || h <= 0)
        {
            return "No pudimos medir el archivo. Prueba con otro.";
        }

        if (Math.Abs((double)w / h / AspectRatio - 1) > AspectTolerance)
        {
            return "El fondo debe ser horizontal 16:9, por ejemplo 1920 × 1080.";
        }

        var maxWidth = kind == MediaKind.Video ? VideoMaxWidth : ImageMaxWidth;
        if (w < MinWidth || w > maxWidth)
        {
            var minHeight = (int)Math.Round(MinWidth / AspectRatio);
            var maxHeight = (int)Math.Round(maxWidth / AspectRatio);
            return $"El fondo debe medir entre {MinWidth} × {minHeight} y {maxWidth} × {maxHeight} (recomendado 1920 × 1080).";
        }

        if (kind == MediaKind.Video && (durationSeconds ?? double.PositiveInfinity) > VideoMaxSeconds)
        {
            return $"El video de fondo dura como máximo {VideoMaxSeconds} segundos: se repite en bucle.";
        }

        return null;
    }
}
