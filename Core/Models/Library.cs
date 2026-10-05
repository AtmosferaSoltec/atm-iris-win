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

public sealed record MediaAsset(Guid Id, MediaKind Kind, string Title, string Subtitle, TimeSpan? Duration, IReadOnlyList<string> Artwork);
