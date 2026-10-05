using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    /// <summary>Host that serves "signed" file URLs (phase 06). Requests to it never reach the real network.</summary>
    public const string StorageHost = "fake-storage.iris.local";

    /// <summary>Church content routes; null when the route does not exist.</summary>
    private HttpResponseMessage? RouteContent(Ctx c, AuthContext a)
    {
        if (c.Is("GET", "sync", "changes"))
        {
            return SyncChanges(c, a);
        }

        return RouteChurch(c, a)
            ?? RoutePeople(c, a)
            ?? RouteServiceTypes(c, a)
            ?? RouteSongs(c, a)
            ?? RouteMedia(c, a)
            ?? RouteBible(c, a)
            ?? RouteRecords(c, a);
    }

    // Placeholders replaced by the phases that implement each area.
    // ----- GET /sync/changes (api-contract §12) -----

    private sealed record Delivered(long Version, string Kind, Guid Id, bool Deleted, string? Json);

    private HttpResponseMessage SyncChanges(Ctx c, AuthContext a)
    {
        var sinceText = c.Query["since"] ?? "0";
        if (!long.TryParse(sinceText, NumberStyles.None, CultureInfo.InvariantCulture, out var since))
        {
            throw Validation("since", "El cursor no es válido.");
        }

        var limit = 200;
        if (c.Query["limit"] is { } limitText && (!int.TryParse(limitText, out limit) || limit is < 1 or > 500))
        {
            throw Validation("limit", "El límite debe estar entre 1 y 500.");
        }

        var church = a.Church;
        var items = _db.Rows
            .Where(r => r.ChurchId == church.Id && r.Version > since)
            .Select(r => new Delivered(r.Version, r.Kind, r.Id, r.Deleted, r.Json))
            .ToList();
        if (church.Version > since)
        {
            items.Add(new Delivered(church.Version, "church", church.Id, false, null));
        }

        items = [.. items.OrderBy(i => i.Version)];
        var taken = items.Take(limit).ToList();
        var hasMore = items.Count > taken.Count;

        ChurchDto? churchDto = taken.Any(i => i.Kind == "church") ? ToChurchDto(church) : null;

        List<T> Changed<T>(string kind, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) =>
            [.. taken.Where(i => i.Kind == kind && !i.Deleted).Select(i => JsonSerializer.Deserialize(i.Json!, info)!)];

        List<Guid> Deleted(string kind) => [.. taken.Where(i => i.Kind == kind && i.Deleted).Select(i => i.Id)];

        var page = new SyncPageDto(
            churchDto,
            new SyncChangesDto(
                Changed(FakeKind.People, IrisJsonContext.Default.PersonDto),
                Changed(FakeKind.ServiceTypes, IrisJsonContext.Default.ServiceTypeDto),
                Changed(FakeKind.Songs, IrisJsonContext.Default.SongDto),
                Changed(FakeKind.Media, IrisJsonContext.Default.MediaAssetDto),
                Changed(FakeKind.ServiceRecords, IrisJsonContext.Default.ServiceRecordDto)),
            new SyncDeletedDto(
                Deleted(FakeKind.People),
                Deleted(FakeKind.ServiceTypes),
                Deleted(FakeKind.Songs),
                Deleted(FakeKind.Media),
                Deleted(FakeKind.ServiceRecords)),
            (taken.Count > 0 ? taken[^1].Version : since).ToString(CultureInfo.InvariantCulture),
            hasMore);
        return Ok(page, IrisJsonContext.Default.SyncPageDto);
    }

    private static ChurchDto ToChurchDto(FakeChurchRow church) => new(
        church.Id,
        church.Name,
        church.Timezone,
        new ChurchModulesDto(church.Bible, church.Multimedia, church.TimeControl),
        new StorageDto(church.UsedBytes, 5L * 1024 * 1024 * 1024),
        church.CreatedAt,
        church.UpdatedAt);
}
