using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Iris.Core.Formatting;
using Iris.Core.Media;
using Iris.Core.Models;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Services.Live;

/// <summary>
/// The church library from the local copy. In the console songs and media are read-only: they are created and edited on
/// the web and arrive through sync. Media carries the state of its cached file.
/// </summary>
public sealed class LiveLibraryRepository(LiveData data, IMediaCache cache) : ILibraryRepository
{
    public async Task<IReadOnlyList<LyricSheet>> LyricsAsync() =>
        (await data.Store.GetSongsAsync())
            .OrderBy(s => NameKey.For(s.Title), StringComparer.Ordinal)
            .Select(Mapping.ToModel)
            .ToList();

    public async Task<IReadOnlyList<MediaAsset>> MediaAsync(MediaKind kind)
    {
        var wire = Mapping.ToWire(kind);
        return (await data.Store.GetMediaAsync())
            .Where(m => m.Kind == wire)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => Mapping.ToModel(m, cache.StateOf(m)))
            .ToList();
    }

    public Task DownloadAsync(IEnumerable<Guid> ids) => cache.RequestAsync(ids);
}

/// <summary>The six built-in gradients plus the church's pictures marked as backgrounds that are already on this PC.</summary>
public sealed class LiveBackgroundRepository(LiveData data, IMediaCache cache) : IBackgroundRepository
{
    public async Task<IReadOnlyList<ProjectionBackground>> BackgroundsAsync()
    {
        var list = new List<ProjectionBackground>(BuiltInBackgrounds.All);
        foreach (var media in (await data.Store.GetMediaAsync()).Where(m => m.Kind == "image" && m.IsBackground).OrderByDescending(m => m.CreatedAt))
        {
            var state = cache.StateOf(media);
            if (state.Availability == MediaAvailability.Ready && state.Path is { } path)
            {
                list.Add(new ProjectionBackground($"media-{media.Id}", media.Title, ["#15151F", "#07070B"], false, path));
            }
        }

        return list;
    }
}
