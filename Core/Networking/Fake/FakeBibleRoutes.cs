using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Iris.Core.Models;
using Iris.Core.Networking.Dto;
using Iris.Core.Services.Mocks;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    private const string BibleCode = "rvr1909";
    private const string BibleName = "Reina-Valera 1909";
    private const int BibleVersion = 1;

    private BibleDownloadDto? _bible;
    private string? _bibleJson;

    // ----- /bible (api-contract §13). Real RVR1909 text only arrives with the real API: here a few passages are real. -----

    private HttpResponseMessage? RouteBible(Ctx c, AuthContext a)
    {
        if (c.Segments is not ["bible", ..])
        {
            return null;
        }

        if (c.Is("GET", "bible", "translations"))
        {
            var translation = new BibleTranslationDto(BibleCode, BibleName, "es", BibleVersion, BibleJson().Length);
            return Ok([translation], IrisJsonContext.Default.ListBibleTranslationDto);
        }

        if (c.Segments is ["bible", "translations", var code, ..] && code != BibleCode)
        {
            throw NotFound("No existe esa traducción.");
        }

        if (c.Is("GET", "bible", "translations", "*", "books"))
        {
            var books = Download().Books.Select(b => new BibleBookDto(b.Id, b.Name, b.Testament, b.ChapterCount, b.Position)).ToList();
            return Ok(books, IrisJsonContext.Default.ListBibleBookDto);
        }

        if (c.Is("GET", "bible", "translations", "*", "books", "*", "chapters", "*"))
        {
            var book = Download().Books.FirstOrDefault(b => b.Id == c.Segments[4]) ?? throw NotFound("No existe ese libro.");
            if (!int.TryParse(c.Segments[6], out var chapter) || chapter < 1 || chapter > book.Chapters.Count)
            {
                throw NotFound("No existe ese capítulo.");
            }

            var verses = book.Chapters[chapter - 1].Select((text, i) => new BibleVerseDto(i + 1, text)).ToList();
            return Ok(new BibleChapterDto(book.Id, chapter, verses), IrisJsonContext.Default.BibleChapterDto);
        }

        if (c.Is("GET", "bible", "translations", "*", "download"))
        {
            var etag = $"\"{BibleVersion}\"";
            if (c.Header("If-None-Match") == etag)
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified) { Headers = { ETag = new EntityTagHeaderValue(etag) } };
            }

            var response = Raw(HttpStatusCode.OK, "{\"data\":" + BibleJson() + "}");
            response.Headers.ETag = new EntityTagHeaderValue(etag);
            return response;
        }

        return null;
    }

    private BibleDownloadDto Download()
    {
        if (_bible is not null)
        {
            return _bible;
        }

        var books = new List<BibleBookDownloadDto>();
        for (var i = 0; i < SampleData.BibleBooks.Count; i++)
        {
            var sample = SampleData.BibleBooks[i];
            var chapters = Enumerable.Range(1, sample.ChapterCount)
                .Select(chapter => (IReadOnlyList<string>)SampleBible.Verses(sample.Id, chapter).Select(v => v.Text).ToList())
                .ToList();
            books.Add(new BibleBookDownloadDto(SampleBible.Usfm[i], sample.Name, sample.Testament == Testament.Old ? "old" : "new", sample.ChapterCount, i + 1, chapters));
        }

        return _bible = new BibleDownloadDto(BibleCode, BibleName, BibleVersion, books);
    }

    private string BibleJson() => _bibleJson ??= JsonSerializer.Serialize(Download(), IrisJsonContext.Default.BibleDownloadDto);
}
