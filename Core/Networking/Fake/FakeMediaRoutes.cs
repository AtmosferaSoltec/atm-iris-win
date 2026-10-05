using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Iris.Core.Formatting;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking.Fake;

public sealed partial class FakeIrisApiHandler
{
    private sealed record PendingUpload(Guid Id, Guid ChurchId, MediaUploadRequestDto Request, DateTimeOffset ExpiresAt)
    {
        public long ReceivedBytes { get; set; } = -1;

        public byte[]? Bytes { get; set; }
    }

    private readonly Dictionary<Guid, PendingUpload> _uploads = [];
    private readonly Dictionary<Guid, byte[]> _fileCache = [];

    private static readonly Dictionary<string, (string[] Types, long MaxBytes)> MediaRules = new()
    {
        ["image"] = (["image/jpeg", "image/png", "image/webp"], 20L * 1024 * 1024),
        ["video"] = (["video/mp4", "video/quicktime"], 2L * 1024 * 1024 * 1024),
        ["audio"] = (["audio/mpeg", "audio/mp4", "audio/aac", "audio/wav", "audio/x-wav"], 200L * 1024 * 1024),
    };

    // ----- /media (api-contract §11) -----

    private HttpResponseMessage? RouteMedia(Ctx c, AuthContext a)
    {
        if (c.Segments is not ["media", ..])
        {
            return null;
        }

        var church = a.Church.Id;
        if (c.Is("POST", "media", "uploads"))
        {
            Require(a, "media.manage");
            return CreateUpload(c, a);
        }

        if (c.Is("POST", "media"))
        {
            Require(a, "media.manage");
            return ConfirmUpload(c, a);
        }

        if (c.Is("GET", "media"))
        {
            return ListMedia(c, church);
        }

        if (c.Is("GET", "media", "*"))
        {
            var media = FakeRows.Find(_db, church, FakeKind.Media, ParseId(c.Segments[1]), IrisJsonContext.Default.MediaAssetDto) ?? throw NotFound();
            return Ok(media, IrisJsonContext.Default.MediaAssetDto);
        }

        if (c.Is("GET", "media", "*", "download-url"))
        {
            var media = FakeRows.Find(_db, church, FakeKind.Media, ParseId(c.Segments[1]), IrisJsonContext.Default.MediaAssetDto) ?? throw NotFound();
            var url = new DownloadUrlDto($"https://{StorageHost}/files/{media.Id}?v={media.UpdatedAt.UtcTicks}", _now().AddHours(1));
            return Ok(url, IrisJsonContext.Default.DownloadUrlDto);
        }

        if (c.Is("PATCH", "media", "*"))
        {
            Require(a, "media.manage");
            var id = ParseId(c.Segments[1]);
            var media = FakeRows.Find(_db, church, FakeKind.Media, id, IrisJsonContext.Default.MediaAssetDto) ?? throw NotFound();
            var patch = c.Body(IrisJsonContext.Default.MediaPatchDto);
            if (patch.Title is { } title && title.Trim().Length is 0 or > 120)
            {
                throw Validation("title", "El título debe tener entre 1 y 120 caracteres.");
            }

            if (patch.IsBackground == true && media.Kind != "image")
            {
                throw Validation("isBackground", "Solo las imágenes pueden ser fondos.");
            }

            media = media with
            {
                Title = patch.Title?.Trim() ?? media.Title,
                Description = patch.Description ?? media.Description,
                IsBackground = patch.IsBackground ?? media.IsBackground,
                UpdatedAt = _now(),
            };
            FakeRows.Put(_db, church, FakeKind.Media, id, media, IrisJsonContext.Default.MediaAssetDto);
            return Ok(media, IrisJsonContext.Default.MediaAssetDto);
        }

        if (c.Is("DELETE", "media", "*"))
        {
            Require(a, "media.manage");
            var id = ParseId(c.Segments[1]);
            var media = FakeRows.Find(_db, church, FakeKind.Media, id, IrisJsonContext.Default.MediaAssetDto) ?? throw NotFound();
            FakeRows.SoftDelete(_db, church, FakeKind.Media, id);
            a.Church.UsedBytes = Math.Max(0, a.Church.UsedBytes - media.SizeBytes);
            _fileCache.Remove(id);
            return NoContent();
        }

        return null;
    }

    private HttpResponseMessage ListMedia(Ctx c, Guid church)
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

        var items = FakeRows.Live(_db, church, FakeKind.Media, IrisJsonContext.Default.MediaAssetDto);
        if (c.Query["kind"] is { Length: > 0 } kind)
        {
            items = items.Where(m => m.Kind == kind);
        }

        if (c.Query["isBackground"] is { Length: > 0 } background)
        {
            items = items.Where(m => m.IsBackground == (background == "true"));
        }

        if (NameKey.For(c.Query["search"]) is { Length: > 0 } search)
        {
            items = items.Where(m => NameKey.For(m.Title).Contains(search, StringComparison.Ordinal)
                || NameKey.For(m.Description).Contains(search, StringComparison.Ordinal));
        }

        var all = items.OrderByDescending(m => m.CreatedAt).ToList();
        return Page(all.Skip((page - 1) * limit).Take(limit).ToList(), page, limit, all.Count, IrisJsonContext.Default.ListMediaAssetDto);
    }

    private HttpResponseMessage CreateUpload(Ctx c, AuthContext a)
    {
        var body = c.Body(IrisJsonContext.Default.MediaUploadRequestDto);
        if (!MediaRules.TryGetValue(body.Kind ?? string.Empty, out var rule))
        {
            throw Validation("kind", "El tipo de archivo no es válido.");
        }

        if (body.FileName?.Trim().Length is null or 0 or > 255 || body.SizeBytes <= 0)
        {
            throw Validation("fileName", "El archivo no es válido.");
        }

        if (!rule.Types.Contains(body.ContentType))
        {
            throw new FakeHttpException(415, "UNSUPPORTED_MEDIA_TYPE", "Ese tipo de archivo no está permitido.");
        }

        if (body.SizeBytes > rule.MaxBytes)
        {
            throw new FakeHttpException(413, "FILE_TOO_LARGE", "El archivo es demasiado grande.");
        }

        const long quota = 5L * 1024 * 1024 * 1024;
        if (a.Church.UsedBytes + body.SizeBytes > quota)
        {
            throw new FakeHttpException(413, "STORAGE_QUOTA_EXCEEDED", "Tu iglesia no tiene espacio suficiente.");
        }

        var upload = new PendingUpload(Guid.NewGuid(), a.Church.Id, body, _now().AddHours(1));
        _uploads[upload.Id] = upload;
        var ticket = new UploadTicketDto(
            upload.Id,
            $"https://{StorageHost}/upload/{upload.Id}",
            new Dictionary<string, string> { ["Content-Type"] = body.ContentType },
            upload.ExpiresAt);
        return Created(ticket, IrisJsonContext.Default.UploadTicketDto);
    }

    private HttpResponseMessage ConfirmUpload(Ctx c, AuthContext a)
    {
        var body = c.Body(IrisJsonContext.Default.MediaConfirmDto);
        var title = body.Title?.Trim() ?? string.Empty;
        if (title.Length is 0 or > 120)
        {
            throw Validation("title", "El título debe tener entre 1 y 120 caracteres.");
        }

        if (!_uploads.TryGetValue(body.UploadId, out var upload) || upload.ChurchId != a.Church.Id || upload.ExpiresAt < _now()
            || upload.ReceivedBytes != upload.Request.SizeBytes)
        {
            throw new FakeHttpException(400, "UPLOAD_NOT_FOUND", "No encontramos el archivo subido. Inténtalo de nuevo.");
        }

        if (body.IsBackground == true && upload.Request.Kind != "image")
        {
            throw Validation("isBackground", "Solo las imágenes pueden ser fondos.");
        }

        var now = _now();
        var media = new MediaAssetDto(
            Guid.NewGuid(),
            upload.Request.Kind,
            title,
            body.Description,
            upload.Request.FileName,
            upload.Request.ContentType,
            upload.Request.SizeBytes,
            body.DurationSeconds,
            body.Width,
            body.Height,
            body.IsBackground ?? false,
            now,
            now);
        FakeRows.Put(_db, a.Church.Id, FakeKind.Media, media.Id, media, IrisJsonContext.Default.MediaAssetDto);
        _fileCache[media.Id] = upload.Bytes ?? [];
        a.Church.UsedBytes += media.SizeBytes;
        _uploads.Remove(upload.Id);
        return Created(media, IrisJsonContext.Default.MediaAssetDto);
    }

    // ----- Fake object storage (the signed URLs of the contract) -----

    private HttpResponseMessage HandleStorage(HttpRequestMessage request, Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (request.Method == HttpMethod.Put && segments is ["upload", var uploadId] && Guid.TryParse(uploadId, out var id) && _uploads.TryGetValue(id, out var upload))
        {
            var bytes = request.Content is null ? [] : request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            upload.Bytes = bytes;
            upload.ReceivedBytes = bytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (request.Method == HttpMethod.Get && segments is ["files", var fileId] && Guid.TryParse(fileId, out var mediaId))
        {
            var row = _db.Rows.FirstOrDefault(r => r.Kind == FakeKind.Media && r.Id == mediaId && !r.Deleted)
                ?? throw new FakeHttpException(404, "NOT_FOUND", "El archivo no existe.");
            var media = System.Text.Json.JsonSerializer.Deserialize(row.Json, IrisJsonContext.Default.MediaAssetDto)!;
            var bytes = FileBytes(media);
            return FileResponse(request, bytes, media.ContentType);
        }

        throw new FakeHttpException(404, "NOT_FOUND", "El archivo no existe.");
    }

    private byte[] FileBytes(MediaAssetDto media)
    {
        if (_fileCache.TryGetValue(media.Id, out var cached))
        {
            return cached;
        }

        var bytes = media.ContentType switch
        {
            "image/png" => FakeSamples.GradientPng(media.Width ?? 1920, media.Height ?? 1080, FakeSamples.ColorFor(media.Id, 0), FakeSamples.ColorFor(media.Id, 1)),
            "audio/wav" or "audio/x-wav" => FakeSamples.ToneWav(media.DurationSeconds ?? 10, 440),
            _ => new byte[Math.Min(media.SizeBytes, 1024 * 1024)],
        };
        return _fileCache[media.Id] = bytes;
    }

    /// <summary>Serves bytes honouring <c>Range</c> (downloads resume after a restart).</summary>
    private static HttpResponseMessage FileResponse(HttpRequestMessage request, byte[] bytes, string contentType)
    {
        var range = request.Headers.Range?.Ranges.FirstOrDefault();
        if (range?.From is { } from && from < bytes.Length)
        {
            var slice = bytes.AsMemory((int)from).ToArray();
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(slice) };
            partial.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, bytes.Length - 1, bytes.Length);
            return partial;
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }
}
