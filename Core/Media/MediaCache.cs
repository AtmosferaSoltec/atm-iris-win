using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Persistence;
using Iris.Core.Services;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Media;

/// <summary>Where one media file stands (see <see cref="IMediaCache.StateOf"/>).</summary>
public sealed record MediaFileState(MediaAvailability Availability, double Progress, string? Path)
{
    public static readonly MediaFileState Missing = new(MediaAvailability.NotDownloaded, 0, null);
}

/// <summary>The offline cache of church files, as the rest of the app sees it.</summary>
public interface IMediaCache
{
    MediaFileState StateOf(MediaAssetDto media);

    /// <summary>Raised on any thread with a media id when its file state changed.</summary>
    event EventHandler<Guid>? StateChanged;

    /// <summary>Raised on any thread when <see cref="LowDiskSpace"/> changes.</summary>
    event EventHandler? LowDiskSpaceChanged;

    /// <summary>Less than 1 GB free: video downloads are paused (the sync indicator says so).</summary>
    bool LowDiskSpace { get; }

    /// <summary>Downloads what is missing or changed and deletes what is gone, in the background. Never throws.</summary>
    Task ReconcileAsync(CancellationToken ct = default);
}

public sealed class NullMediaCache : IMediaCache
{
    public bool LowDiskSpace => false;

#pragma warning disable CS0067 // Never raised: Mock mode has no files.
    public event EventHandler<Guid>? StateChanged;

    public event EventHandler? LowDiskSpaceChanged;
#pragma warning restore CS0067

    public MediaFileState StateOf(MediaAssetDto media) => new(MediaAvailability.Placeholder, 0, null);

    public Task ReconcileAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Free disk space for the cache folder (replaceable in tests).</summary>
public interface IDiskSpace
{
    long FreeBytes(string folder);
}

public sealed class DriveDiskSpace : IDiskSpace
{
    public long FreeBytes(string folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }
}

/// <summary>
/// Church images, videos and music kept on this PC so the service never depends on the network. Layout:
/// <c>root/Media/&lt;churchId&gt;/&lt;mediaId&gt;-&lt;updatedAt ticks&gt;.&lt;ext&gt;</c>. After every sync it downloads what is missing
/// (two at a time; images and music first, videos after) through the signed URL of <c>GET /media/:id/download-url</c> with a
/// client that carries no credentials, into a <c>.part</c> file that is renamed when complete and resumed after a restart.
/// Files of deleted or replaced media are removed.
/// </summary>
public sealed class MediaCache : IMediaCache
{
    /// <summary>Below this much free space, videos are not downloaded.</summary>
    public const long MinimumFreeBytes = 1L * 1024 * 1024 * 1024;

    private const int MaxParallel = 2;

    private readonly LocalStore _store;
    private readonly ApiClient _api;
    private readonly Func<HttpClient> _downloader;
    private readonly ISessionContext _session;
    private readonly string _root;
    private readonly IDiskSpace _disk;
    private readonly ILogger<MediaCache> _log;
    private readonly ConcurrentDictionary<Guid, MediaFileState> _active = new();
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);
    private bool _lowDisk;

    /// <param name="downloader">A client <b>without</b> the auth handler: the URL is already signed.</param>
    public MediaCache(
        LocalStore store,
        ApiClient api,
        Func<HttpClient> downloader,
        ISessionContext session,
        string root,
        IDiskSpace? disk,
        ILogger<MediaCache> log)
    {
        _store = store;
        _api = api;
        _downloader = downloader;
        _session = session;
        _root = root;
        _disk = disk ?? new DriveDiskSpace();
        _log = log;
    }

    public event EventHandler<Guid>? StateChanged;

    public event EventHandler? LowDiskSpaceChanged;

    public bool LowDiskSpace => _lowDisk;

    public MediaFileState StateOf(MediaAssetDto media)
    {
        if (_active.TryGetValue(media.Id, out var active))
        {
            return active;
        }

        var path = PathFor(media);
        return path is not null && File.Exists(path) ? new MediaFileState(MediaAvailability.Ready, 1, path) : MediaFileState.Missing;
    }

    public string? PathFor(MediaAssetDto media) =>
        _session.Current is { } session ? Path.Combine(ChurchFolder(session.Church.Id), FileName(media)) : null;

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        if (_session.Current is not { } session)
        {
            return;
        }

        await _reconcileGate.WaitAsync(ct);
        try
        {
            var media = await _store.GetMediaAsync();
            var folder = ChurchFolder(session.Church.Id);
            Directory.CreateDirectory(folder);
            RemoveStale(session.Church.Id, folder, media);

            var missing = media
                .Where(m => !File.Exists(Path.Combine(folder, FileName(m))))
                .OrderBy(m => m.Kind == "video" ? 1 : 0)
                .ThenByDescending(m => m.CreatedAt)
                .ToList();
            foreach (var item in missing)
            {
                Set(item.Id, MediaFileState.Missing);
            }

            using var slots = new SemaphoreSlim(MaxParallel);
            var tasks = missing.Select(async item =>
            {
                await slots.WaitAsync(ct);
                try
                {
                    await DownloadAsync(item, folder, ct);
                }
                finally
                {
                    slots.Release();
                }
            });
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.LogWarning("La caché de archivos no pudo actualizarse: {Error}", ex.Message);
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task DownloadAsync(MediaAssetDto item, string folder, CancellationToken ct)
    {
        var final = Path.Combine(folder, FileName(item));
        var part = final + ".part";
        try
        {
            if (item.Kind == "video")
            {
                var free = _disk.FreeBytes(folder);
                SetLowDisk(free < MinimumFreeBytes + item.SizeBytes);
                if (_lowDisk)
                {
                    _log.LogWarning("Poco espacio en disco: se pausa la descarga del video {Id}", item.Id);
                    _active.TryRemove(item.Id, out _);
                    return;
                }
            }

            Set(item.Id, new MediaFileState(MediaAvailability.Downloading, 0, null));
            var ticket = await _api.SendAsync(ApiRequest.Get($"media/{item.Id}/download-url"), IrisJsonContext.Default.DownloadUrlDto, ct);

            var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, ticket.Url);
            if (existing > 0 && existing < item.SizeBytes)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }
            else
            {
                existing = 0;
            }

            using var response = await _downloader().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var resumed = response.StatusCode == HttpStatusCode.PartialContent;
            var total = item.SizeBytes > 0 ? item.SizeBytes : (response.Content.Headers.ContentLength ?? 0) + (resumed ? existing : 0);
            var written = resumed ? existing : 0;

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                var lastReport = 0.0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    var progress = total > 0 ? Math.Min(1, (double)written / total) : 0;
                    if (progress - lastReport >= 0.02)
                    {
                        lastReport = progress;
                        Set(item.Id, new MediaFileState(MediaAvailability.Downloading, progress, null));
                    }
                }
            }

            File.Move(part, final, overwrite: true);
            Set(item.Id, new MediaFileState(MediaAvailability.Ready, 1, final));
            _active.TryRemove(item.Id, out _);
            _log.LogInformation("Archivo {Id} descargado ({Bytes} bytes)", item.Id, written);
        }
        catch (OperationCanceledException)
        {
            _active.TryRemove(item.Id, out _);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or NetworkException or ApiException or IOException)
        {
            _log.LogWarning("No se pudo descargar el archivo {Id}: {Reason}", item.Id, ex.Message);
            Set(item.Id, new MediaFileState(MediaAvailability.Failed, 0, null));
        }
    }

    private void RemoveStale(Guid churchId, string churchFolder, IReadOnlyList<MediaAssetDto> media)
    {
        try
        {
            var expected = media.Select(FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(churchFolder))
            {
                var name = Path.GetFileName(file);
                var baseName = name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
                if (!expected.Contains(baseName))
                {
                    File.Delete(file);
                }
            }

            // Files of other churches (after a church switch) are of no use here.
            foreach (var other in Directory.EnumerateDirectories(Path.Combine(_root, "Media")))
            {
                if (!string.Equals(Path.GetFileName(other), churchId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(other, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("No se pudieron borrar archivos antiguos: {Error}", ex.Message);
        }
    }

    private string ChurchFolder(Guid churchId) => Path.Combine(_root, "Media", churchId.ToString());

    private static string FileName(MediaAssetDto media)
    {
        var extension = Path.GetExtension(media.FileName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = media.ContentType switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                "video/mp4" => ".mp4",
                "video/quicktime" => ".mov",
                "audio/mpeg" => ".mp3",
                "audio/wav" or "audio/x-wav" => ".wav",
                "audio/aac" => ".aac",
                _ => ".bin",
            };
        }

        return $"{media.Id}-{media.UpdatedAt.UtcTicks}{extension.ToLowerInvariant()}";
    }

    private void Set(Guid id, MediaFileState state)
    {
        _active[id] = state;
        StateChanged?.Invoke(this, id);
    }

    private void SetLowDisk(bool value)
    {
        if (_lowDisk != value)
        {
            _lowDisk = value;
            LowDiskSpaceChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
