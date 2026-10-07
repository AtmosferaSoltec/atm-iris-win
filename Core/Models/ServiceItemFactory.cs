using System;
using System.Collections.Generic;
using System.Linq;

namespace Iris.Core.Models;

/// <summary>Library → service conversions (IRIS_SPEC §9).</summary>
public static class ServiceItemFactory
{
    public static ServiceItem FromLyrics(LyricSheet sheet) =>
        new(Guid.NewGuid(), ServiceItemKind.Song, sheet.Title, sheet.Author, sheet.Sections.Select(s => s with { Id = Guid.NewGuid() }).ToList());

    public static ServiceItem FromMedia(MediaAsset asset) => FromMediaCore(asset) with { MediaId = asset.Id };

    private static ServiceItem FromMediaCore(MediaAsset asset) => asset.Kind switch
    {
        MediaKind.Music => new(Guid.NewGuid(), ServiceItemKind.Music, asset.Title,
            $"{asset.Subtitle} · {DurationText.Format(asset.Duration ?? TimeSpan.Zero)}",
            [Slide.Create(new AudioContent(asset.Title, asset.Duration ?? TimeSpan.Zero, asset.LocalPath))]),
        MediaKind.Video => new(Guid.NewGuid(), ServiceItemKind.Video, asset.Title,
            $"Video · {DurationText.Format(asset.Duration ?? TimeSpan.Zero)}",
            [Slide.Create(new VideoContent(asset.Title, asset.Duration ?? TimeSpan.Zero, asset.LocalPath))]),
        _ => new(Guid.NewGuid(), ServiceItemKind.Image, asset.Title, asset.Subtitle,
            [Slide.Create(new ImageContent(asset.Title, asset.Artwork, asset.LocalPath))]),
    };

    public static ServiceItem FromScripture(BibleBook book, int chapter, IReadOnlyList<BibleVerse> verses, string translation) =>
        new(Guid.NewGuid(), ServiceItemKind.Scripture, $"{book.Name} {chapter}", translation,
            verses.Select(v => Slide.Create(new TextContent(v.Text, $"{book.Name} {chapter}:{v.Number}"), $"Versículo {v.Number}")).ToList());
}

public static class DurationText
{
    /// <summary>"2:45", "10:00", "1:02:03".</summary>
    public static string Format(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    public static string Format(double seconds) => Format(TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds))));
}
