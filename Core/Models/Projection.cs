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
public sealed record ImageContent(string Title, IReadOnlyList<string> Artwork, string? LocalPath = null) : ProjectionContent;

public sealed record VideoContent(string Title, TimeSpan Duration, string? LocalPath = null) : ProjectionContent;

/// <summary>Music; only drawn on cards, never sent to the TV.</summary>
public sealed record AudioContent(string Title, TimeSpan Duration, string? LocalPath = null) : ProjectionContent;

public sealed record LogoContent(string Name) : ProjectionContent;

/// <summary>A gradient, or a picture of the church ("ImagePath", drawn UniformToFill under the 20 % veil) over a gradient fallback.</summary>
public sealed record ProjectionBackground(string Id, string Name, IReadOnlyList<string> Colors, bool IsAnimated, string? ImagePath = null);

/// <summary>A full TV frame. A null <see cref="Background"/> means plain black.</summary>
public sealed record ProjectionFrame(ProjectionBackground? Background, ProjectionContent Content)
{
    public static readonly ProjectionFrame Black = new(null, ProjectionContent.Blank);
}

/// <summary>A secondary monitor that can host the TV window. <see cref="Id"/> is stable while the monitor stays connected.</summary>
public sealed record ExternalDisplay(string Name, string Resolution, string Id = "");
