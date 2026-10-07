using System;
using System.Collections.Generic;

namespace Iris.Core.Models;

public enum ServiceItemKind
{
    Song,
    Scripture,
    Announcement,
    Music,
    Image,
    Video,
}

public sealed record Slide(Guid Id, string? Label, ProjectionContent Content)
{
    public static Slide Create(ProjectionContent content, string? label = null) => new(Guid.NewGuid(), label, content);
}

public sealed record ServiceItem(Guid Id, ServiceItemKind Kind, string Title, string Subtitle, IReadOnlyList<Slide> Slides)
{
    public bool IsMedia => Kind is ServiceItemKind.Music or ServiceItemKind.Video or ServiceItemKind.Image;

    /// <summary>The library file behind music, image and video items, so the console can follow its download
    /// and use the file once it is on this PC (api-contract §11). Null for text and design data.</summary>
    public Guid? MediaId { get; init; }

    public ServiceItem Duplicate() => this with { Id = Guid.NewGuid() };
}

public sealed record ServicePlan(Guid Id, string Title, DateTimeOffset Date, IReadOnlyList<ServiceItem> Items);

public sealed record RemovedItem(ServiceItem Item, int Index);

public static class ProjectionContentExtensions
{
    /// <summary>The cached file of a media slide; null for text, or while the file is not on this PC.</summary>
    public static string? LocalPath(this ProjectionContent content) => content switch
    {
        ImageContent image => image.LocalPath,
        VideoContent video => video.LocalPath,
        AudioContent audio => audio.LocalPath,
        _ => null,
    };

    /// <summary>The same media with another cached file (or none). Text stays as it is.</summary>
    public static ProjectionContent WithLocalPath(this ProjectionContent content, string? path) => content switch
    {
        ImageContent image => image with { LocalPath = path },
        VideoContent video => video with { LocalPath = path },
        AudioContent audio => audio with { LocalPath = path },
        _ => content,
    };
}
