using System;
using System.Collections.Generic;
using System.Linq;

namespace Iris.Core.Models;

public enum Testament
{
    Old,
    New,
}

public sealed record BibleBook(string Id, string Name, Testament Testament, int ChapterCount);

public sealed record BibleVerse(int Number, string Text);

public sealed record LyricSheet(Guid Id, string Title, string Author, IReadOnlyList<Slide> Sections)
{
    public string FirstLine
    {
        get
        {
            var body = Sections.Select(s => s.Content).OfType<TextContent>().FirstOrDefault()?.Body ?? string.Empty;
            var newline = body.IndexOf('\n');
            return (newline >= 0 ? body[..newline] : body).TrimEnd(',', ';', ' ');
        }
    }
}

public enum MediaKind
{
    Music,
    Image,
    Video,
}

/// <summary>Where a library file stands on this PC.</summary>
public enum MediaAvailability
{
    /// <summary>Design data without a file: usable, drawn as a placeholder.</summary>
    Placeholder,
    NotDownloaded,
    Downloading,
    Ready,
    Failed,
}

public sealed record MediaAsset(Guid Id, MediaKind Kind, string Title, string Subtitle, TimeSpan? Duration, IReadOnlyList<string> Artwork)
{
    /// <summary>The cached file once <see cref="Availability"/> is <see cref="MediaAvailability.Ready"/>.</summary>
    public string? LocalPath { get; init; }

    public MediaAvailability Availability { get; init; } = MediaAvailability.Placeholder;

    /// <summary>0…1 while <see cref="MediaAvailability.Downloading"/>.</summary>
    public double Progress { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    /// <summary>Images only: offered in the background picker.</summary>
    public bool IsBackground { get; init; }

    /// <summary>The file is on this PC (or it is design data): it can play or go to the TV. Music and videos can be
    /// added to a service before that; adding them starts the download.</summary>
    public bool IsAvailable => Availability is MediaAvailability.Placeholder or MediaAvailability.Ready;
}
