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

    public ServiceItem Duplicate() => this with { Id = Guid.NewGuid() };
}

public sealed record ServicePlan(Guid Id, string Title, DateTimeOffset Date, IReadOnlyList<ServiceItem> Items);

public sealed record RemovedItem(ServiceItem Item, int Index);
