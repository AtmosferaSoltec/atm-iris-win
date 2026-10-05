using System;
using System.Collections.Generic;

namespace Iris.Core.Models;

/// <summary>What a projection frame shows on top of its background.</summary>
public abstract record ProjectionContent
{
    public static readonly ProjectionContent Blank = new BlankContent();
}

public sealed record BlankContent : ProjectionContent;

public sealed record TextContent(string Body, string? Footnote = null) : ProjectionContent;

/// <summary>Image placeholder: <paramref name="Artwork"/> are hex colors painted as a gradient.</summary>
public sealed record ImageContent(string Title, IReadOnlyList<string> Artwork) : ProjectionContent;

public sealed record VideoContent(string Title, TimeSpan Duration) : ProjectionContent;

/// <summary>Music; only drawn on cards, never sent to the TV.</summary>
public sealed record AudioContent(string Title, TimeSpan Duration) : ProjectionContent;

public sealed record LogoContent(string Name) : ProjectionContent;

public sealed record ProjectionBackground(string Id, string Name, IReadOnlyList<string> Colors, bool IsAnimated);

/// <summary>A full TV frame. A null <see cref="Background"/> means plain black.</summary>
public sealed record ProjectionFrame(ProjectionBackground? Background, ProjectionContent Content)
{
    public static readonly ProjectionFrame Black = new(null, ProjectionContent.Blank);
}

public sealed record ExternalDisplay(string Name, string Resolution);
