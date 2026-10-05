using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Services;
using Iris.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Bible;

/// <summary>
/// The complete Reina-Valera 1909 on this PC (api-contract §13): downloaded once from
/// <c>GET /bible/translations/rvr1909/download</c> into <c>Bible/rvr1909-v&lt;version&gt;.json</c> with its ETag, kept in memory for
/// instant lookups, and rechecked with <c>If-None-Match</c> at most once a day. Everything the picker needs works offline.
/// </summary>
public sealed class BibleStore
{
    public const string Code = "rvr1909";
    public const string DefaultName = "Reina-Valera 1909";
    private static readonly TimeSpan RecheckEvery = TimeSpan.FromHours(24);

    private readonly ApiClient _api;
    private readonly string _folder;
    private readonly ILogger<BibleStore> _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BibleDownloadDto? _data;
    private IReadOnlyList<BibleBook> _books = [];
    private Dictionary<string, BibleBookDownloadDto> _byId = [];
    private BibleStatus _status = new(BiblePhase.NotDownloaded);

    public BibleStore(ApiClient api, string folder, ILogger<BibleStore> log, Func<DateTimeOffset>? now = null)
    {
        _api = api;
        _folder = Path.Combine(folder, "Bible");
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public BibleStatus Status => _status;

    public string TranslationName => _data?.Name ?? DefaultName;

    /// <summary>Raised on any thread when <see cref="Status"/> changes.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// Loads the file from disk if there is one, then downloads or rechecks as needed. Never throws: problems end up in
    /// <see cref="Status"/> (only when there is no local copy: with one, the Bible keeps working).
    /// </summary>
    public async Task EnsureAsync(bool force = false, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var meta = ReadMeta();
            if (_data is null && meta is not null)
            {
                _data = await LoadFileAsync(meta.Version, ct);
                if (_data is null)
                {
                    meta = null; // corrupt or missing: download again
                }
                else
                {
                    Index(_data);
                }
            }

            if (_data is not null)
            {
                SetStatus(BibleStatus.Ready);
                if (!force && meta is not null && _now() - meta.CheckedAt < RecheckEvery)
                {
                    return;
                }
            }
            else
            {
                SetStatus(new BibleStatus(BiblePhase.Downloading));
            }

            await DownloadAsync(meta, ct);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<BibleBook>> BooksAsync() => Task.FromResult(_books);

    public int VerseCount(string bookId, int chapter) =>
        _byId.TryGetValue(bookId, out var book) && chapter >= 1 && chapter <= book.Chapters.Count ? book.Chapters[chapter - 1].Count : 0;

    public IReadOnlyList<BibleVerse> Verses(string bookId, int chapter) =>
        _byId.TryGetValue(bookId, out var book) && chapter >= 1 && chapter <= book.Chapters.Count
            ? book.Chapters[chapter - 1].Select((text, i) => new BibleVerse(i + 1, text)).ToList()
            : [];

    private async Task DownloadAsync(BibleMetaDto? meta, CancellationToken ct)
    {
        try
        {
            var sizeHint = 0L;
            if (_data is null)
            {
                var translations = await _api.SendAsync(ApiRequest.Get("bible/translations"), IrisJsonContext.Default.ListBibleTranslationDto, ct);
                sizeHint = translations.FirstOrDefault(t => t.Code == Code)?.SizeBytes ?? 0;
            }

            var etagToSend = _data is null ? null : meta?.ETag;
            var result = await _api.GetAsync(
                $"bible/translations/{Code}/download",
                etagToSend,
                async response =>
                {
                    if (response.StatusCode == HttpStatusCode.NotModified)
                    {
                        return (Data: (BibleDownloadDto?)null, ETag: etagToSend);
                    }

                    var total = sizeHint > 0 ? sizeHint : response.Content.Headers.ContentLength ?? 0;
                    await using var stream = new CountingStream(await response.Content.ReadAsStreamAsync(ct), read =>
                    {
                        if (_data is null && total > 0)
                        {
                            SetStatus(new BibleStatus(BiblePhase.Downloading, Math.Min(0.99, (double)read / total)));
                        }
                    });
                    var envelope = await JsonSerializer.DeserializeAsync(stream, IrisJsonContext.Default.BibleEnvelopeDto, ct);
                    return (Data: envelope?.Data, ETag: response.Headers.ETag?.Tag);
                },
                ct);

            if (result.Data is { } fresh)
            {
                await SaveFileAsync(fresh, ct);
                WriteMeta(new BibleMetaDto(fresh.Version, result.ETag, _now()));
                _data = fresh;
                Index(fresh);
                _log.LogInformation("Biblia {Name} v{Version} descargada", fresh.Name, fresh.Version);
                SetStatus(BibleStatus.Ready);
            }
            else if (meta is not null)
            {
                WriteMeta(meta with { CheckedAt = _now() });
            }
        }
        catch (NetworkException)
        {
            if (_data is null)
            {
                SetStatus(new BibleStatus(BiblePhase.Failed, 0, "Necesitas conexión para descargar la Biblia la primera vez."));
            }
        }
        catch (ApiException ex)
        {
            _log.LogWarning("La descarga de la Biblia falló: {Status} {Code}", ex.StatusCode, ex.Code);
            if (_data is null)
            {
                SetStatus(new BibleStatus(BiblePhase.Failed, 0, ex.Message));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _log.LogWarning("La descarga de la Biblia no se pudo guardar: {Error}", ex.Message);
            if (_data is null)
            {
                SetStatus(new BibleStatus(BiblePhase.Failed, 0, "No pudimos descargar la Biblia. Inténtalo de nuevo."));
            }
        }
    }

    private void Index(BibleDownloadDto data)
    {
        _books = data.Books
            .OrderBy(b => b.Position)
            .Select(b => new BibleBook(b.Id, b.Name, b.Testament == "old" ? Testament.Old : Testament.New, b.ChapterCount))
            .ToList();
        _byId = data.Books.ToDictionary(b => b.Id);
    }

    private string FilePath(int version) => Path.Combine(_folder, $"{Code}-v{version}.json");

    private string MetaPath => Path.Combine(_folder, $"{Code}.meta.json");

    private BibleMetaDto? ReadMeta()
    {
        try
        {
            return File.Exists(MetaPath) ? JsonSerializer.Deserialize(File.ReadAllText(MetaPath), IrisJsonContext.Default.BibleMetaDto) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private void WriteMeta(BibleMetaDto meta) =>
        File.WriteAllText(MetaPath, JsonSerializer.Serialize(meta, IrisJsonContext.Default.BibleMetaDto));

    private async Task<BibleDownloadDto?> LoadFileAsync(int version, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(FilePath(version)))
            {
                return null;
            }

            await using var stream = File.OpenRead(FilePath(version));
            return await JsonSerializer.DeserializeAsync(stream, IrisJsonContext.Default.BibleDownloadDto, ct);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.LogWarning("El archivo de la Biblia no se pudo leer: {Error}", ex.Message);
            return null;
        }
    }

    private async Task SaveFileAsync(BibleDownloadDto data, CancellationToken ct)
    {
        Directory.CreateDirectory(_folder);
        var temp = FilePath(data.Version) + ".part";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, data, IrisJsonContext.Default.BibleDownloadDto, ct);
        }

        File.Move(temp, FilePath(data.Version), overwrite: true);
        foreach (var old in Directory.EnumerateFiles(_folder, $"{Code}-v*.json").Where(f => f != FilePath(data.Version)))
        {
            File.Delete(old);
        }
    }

    private void SetStatus(BibleStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CountingStream(Stream inner, Action<long> report) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            _read += read;
            report(_read);
            return read;
        }
    }
}

/// <summary>Downloads (or rechecks) the Bible after a sync round when the Bible module is on.</summary>
public sealed class BibleSyncTask(BibleStore store, IModuleSettingsRepository modules) : IPostSyncTask
{
    public async Task RunAsync(CancellationToken ct)
    {
        if ((await modules.ModulesAsync()).Bible)
        {
            await store.EnsureAsync(ct: ct);
        }
    }
}

/// <summary><see cref="IBibleRepository"/> over the <see cref="BibleStore"/>.</summary>
public sealed class LiveBibleRepository(BibleStore store) : IBibleRepository
{
    public string TranslationName => store.TranslationName;

    public BibleStatus Status => store.Status;

    public event EventHandler? StatusChanged
    {
        add => store.StatusChanged += value;
        remove => store.StatusChanged -= value;
    }

    public Task RetryAsync() => store.EnsureAsync(force: true);

    public async Task<IReadOnlyList<BibleBook>> BooksAsync()
    {
        // The picker may open before the post-sync download finished: load what is on disk first.
        if (store.Status.Phase == BiblePhase.NotDownloaded)
        {
            await store.EnsureAsync();
        }

        return await store.BooksAsync();
    }

    public Task<int> VerseCountAsync(string bookId, int chapter) => Task.FromResult(store.VerseCount(bookId, chapter));

    public Task<IReadOnlyList<BibleVerse>> VersesAsync(string bookId, int chapter) => Task.FromResult(store.Verses(bookId, chapter));
}
