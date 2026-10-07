using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Iris.Core.Formatting;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    // ----- /songs (api-contract §10). The console only reads; the web writes. -----

    private HttpResponseMessage? RouteSongs(Ctx c, AuthContext a)
    {
        if (c.Segments is not ["songs", ..])
        {
            return null;
        }

        var church = a.Church.Id;
        if (c.Is("GET", "songs"))
        {
            return ListSongs(c, church);
        }

        if (c.Is("GET", "songs", "*"))
        {
            var song = FakeRows.Find(_db, church, FakeKind.Songs, ParseId(c.Segments[1]), IrisJsonContext.Default.SongDto) ?? throw NotFound();
            return Ok(song, IrisJsonContext.Default.SongDto);
        }

        // No `POST /songs/import`: lyrics are written one at a time from the web (api-contract §10).

        if (c.Is("POST", "songs"))
        {
            var body = c.Body(IrisJsonContext.Default.SongCreateDto);
            var id = body.Id ?? Guid.NewGuid();
            if (FakeRows.Find(_db, church, FakeKind.Songs, id, IrisJsonContext.Default.SongDto) is { } existing)
            {
                return Ok(existing, IrisJsonContext.Default.SongDto);
            }

            if (FakeRows.BelongsToOtherChurch(_db, church, FakeKind.Songs, id))
            {
                throw new FakeHttpException(409, "ID_CONFLICT", "Ese identificador ya pertenece a otro recurso.");
            }

            return Created(SaveSong(church, id, new SongInputDto(body.Title, body.Author, body.Sections), null), IrisJsonContext.Default.SongDto);
        }

        if (c.Is("PUT", "songs", "*"))
        {
            var id = ParseId(c.Segments[1]);
            var existing = FakeRows.Find(_db, church, FakeKind.Songs, id, IrisJsonContext.Default.SongDto);
            if (existing is null && FakeRows.BelongsToOtherChurch(_db, church, FakeKind.Songs, id))
            {
                throw new FakeHttpException(409, "ID_CONFLICT", "Ese identificador ya pertenece a otro recurso.");
            }

            var saved = SaveSong(church, id, c.Body(IrisJsonContext.Default.SongInputDto), existing);
            return existing is null ? Created(saved, IrisJsonContext.Default.SongDto) : Ok(saved, IrisJsonContext.Default.SongDto);
        }

        if (c.Is("DELETE", "songs", "*"))
        {
            return FakeRows.SoftDelete(_db, church, FakeKind.Songs, ParseId(c.Segments[1])) ? NoContent() : throw NotFound();
        }

        return null;
    }

    private HttpResponseMessage ListSongs(Ctx c, Guid church)
    {
        var page = 1;
        var limit = 20;
        if (c.Query["page"] is { } pageText && (!int.TryParse(pageText, out page) || page < 1))
        {
            throw Validation("page", "La página debe ser 1 o mayor.");
        }

        if (c.Query["limit"] is { } limitText && (!int.TryParse(limitText, out limit) || limit is < 1 or > 100))
        {
            throw Validation("limit", "El límite debe estar entre 1 y 100.");
        }

        var sort = c.Query["sort"] ?? "title";
        if (sort is not ("title" or "-updatedAt"))
        {
            throw Validation("sort", "El orden no es válido.");
        }

        var query = NameKey.For(c.Query["search"]);
        var songs = FakeRows.Live(_db, church, FakeKind.Songs, IrisJsonContext.Default.SongDto).ToList();

        IEnumerable<SongDto> ordered;
        if (query.Length > 0)
        {
            // Relevance: a hit in the title beats one in the author, which beats one in the lyrics.
            ordered = songs
                .Select(s => (Song: s, Score: Score(s, query)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => NameKey.For(x.Song.Title), StringComparer.Ordinal)
                .Select(x => x.Song);
        }
        else
        {
            ordered = sort == "title"
                ? songs.OrderBy(s => NameKey.For(s.Title), StringComparer.Ordinal)
                : songs.OrderByDescending(s => s.UpdatedAt);
        }

        var all = ordered.ToList();
        var items = all.Skip((page - 1) * limit).Take(limit).Select(Summary).ToList();
        return Page(items, page, limit, all.Count, IrisJsonContext.Default.ListSongSummaryDto);
    }

    private static int Score(SongDto song, string query)
    {
        if (NameKey.For(song.Title).Contains(query, StringComparison.Ordinal))
        {
            return 3;
        }

        if (NameKey.For(song.Author).Contains(query, StringComparison.Ordinal))
        {
            return 2;
        }

        return song.Sections.Any(s => NameKey.For(s.Text).Contains(query, StringComparison.Ordinal)) ? 1 : 0;
    }

    private static SongSummaryDto Summary(SongDto song)
    {
        var first = song.Sections.FirstOrDefault()?.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return new SongSummaryDto(song.Id, song.Title, song.Author, song.Sections.Count, string.IsNullOrEmpty(first) ? null : first, song.UpdatedAt);
    }

    private SongDto SaveSong(Guid church, Guid id, SongInputDto input, SongDto? existing)
    {
        var errors = new Dictionary<string, string>();
        var title = input.Title?.Trim() ?? string.Empty;
        var sections = input.Sections ?? [];
        if (title.Length is 0 or > 120)
        {
            errors["title"] = "El título debe tener entre 1 y 120 caracteres.";
        }

        if ((input.Author?.Length ?? 0) > 120)
        {
            errors["author"] = "El autor no puede superar 120 caracteres.";
        }

        if (sections.Count is 0 or > 80)
        {
            errors["sections"] = "Una canción necesita entre 1 y 80 secciones.";
        }

        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].Text?.Length is null or 0 or > 2000)
            {
                errors[$"sections.{i}.text"] = "El texto debe tener entre 1 y 2000 caracteres.";
            }

            if ((sections[i].Label?.Length ?? 0) > 40)
            {
                errors[$"sections.{i}.label"] = "La etiqueta no puede superar 40 caracteres.";
            }
        }

        if (errors.Count > 0)
        {
            throw new FakeHttpException(400, "VALIDATION_FAILED", "Revisa los datos ingresados.", errors);
        }

        var now = _now();
        var dto = new SongDto(
            id,
            title,
            input.Author ?? string.Empty,
            sections.Select((s, i) => new SongSectionDto(existing is not null && i < existing.Sections.Count ? existing.Sections[i].Id : Guid.NewGuid(), s.Label, s.Text)).ToList(),
            existing?.CreatedAt ?? now,
            now);
        FakeRows.Put(_db, church, FakeKind.Songs, id, dto, IrisJsonContext.Default.SongDto);
        return dto;
    }

    /// <summary>Development tool: adds a song to the fake church as if it had been written on the web.</summary>
    public SongDto AddTestSong(Guid churchId)
    {
        lock (_gate)
        {
            var n = FakeRows.Live(_db, churchId, FakeKind.Songs, IrisJsonContext.Default.SongDto).Count() + 1;
            var song = SaveSong(
                churchId,
                Guid.NewGuid(),
                new SongInputDto(
                    $"Canción de prueba {n}",
                    "Autor de prueba",
                    [new SongSectionInputDto("Estrofa 1", "Primera línea de prueba,\nsegunda línea de prueba."), new SongSectionInputDto("Coro", "Aleluya, aleluya,\ncantad al Señor.")]),
                null);
            _store.Save(_db);
            return song;
        }
    }
}
