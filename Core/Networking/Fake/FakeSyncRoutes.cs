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

    private ChurchDto ToChurchDto(FakeChurchRow church)
    {
        // Lo apagado para todo Iris queda apagado aunque la iglesia lo tenga encendido; su elección
        // se conserva y vuelve sola cuando el módulo se habilita otra vez (api-contract §6).
        var available = new ChurchModulesDto(_db.SystemBibleEnabled, true, true);
        var effective = new ChurchModulesDto(church.Bible && available.Bible, church.Multimedia && available.Multimedia, church.TimeControl && available.TimeControl);
        return new ChurchDto(
            church.Id,
            church.Name,
            church.Timezone,
            effective,
            StorageOf(church.Id),
            church.CreatedAt,
            church.UpdatedAt,
            available,
            new ProjectionSettingsDto(church.ProjectionFontFamily, church.ProjectionFontSizePt, church.ProjectionDefaultBackgroundId));
    }

    /// <summary>What the church's live media takes, by section (api-contract §6).</summary>
    private StorageDto StorageOf(Guid church)
    {
        long music = 0, backgrounds = 0, media = 0;
        foreach (var item in FakeRows.Live(_db, church, FakeKind.Media, IrisJsonContext.Default.MediaAssetDto))
        {
            if (item.Kind == "audio")
            {
                music += item.SizeBytes;
            }
            else if (item.IsBackground)
            {
                backgrounds += item.SizeBytes;
            }
            else
            {
                media += item.SizeBytes;
            }
        }

        return new StorageDto(music + backgrounds + media, 5L * 1024 * 1024 * 1024, new StorageBreakdownDto(music, backgrounds, media));
    }
}
