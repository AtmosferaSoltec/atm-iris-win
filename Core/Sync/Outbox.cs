using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Networking;
using Iris.Core.Persistence;
using Microsoft.Extensions.Logging;

namespace Iris.Core.Sync;

/// <summary>A queued write the server refused for good and that was dropped, to tell the person.</summary>
public sealed record DiscardedWrite(string Label, string Message, int StatusCode, StoreEntity? Entity);

public enum FlushOutcome
{
    /// <summary>The queue is empty.</summary>
    Done,

    /// <summary>Stopped on a network error, 5xx, 429 or a dead session: try again later (see <see cref="Outbox.RetryDelay"/>).</summary>
    Blocked,
}

/// <summary>
/// Writes made on this PC, in the order they happened. A repository applies its change to the local copy first (so the
/// screen is right at once) and then enqueues the matching request; <see cref="FlushAsync"/> sends them one by one.
/// Every queued request is idempotent (api-contract §2): created with a client-side id, or a <c>PUT</c>/<c>DELETE</c>,
/// so a retry after a lost answer never duplicates anything.
/// </summary>
public sealed class Outbox
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(5)];

    private readonly LocalStore _store;
    private readonly ApiClient _api;
    private readonly ILogger<Outbox> _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private int _consecutiveFailures;

    public Outbox(LocalStore store, ApiClient api, ILogger<Outbox> log, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _api = api;
        _log = log;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>A queued write was refused and dropped. Raised on any thread.</summary>
    public event EventHandler<DiscardedWrite>? Discarded;

    /// <summary>How long to wait before the next attempt after a blocked flush (5 s, 15 s, 60 s, then 5 min).</summary>
    public TimeSpan RetryDelay => Backoff[Math.Min(Math.Max(_consecutiveFailures - 1, 0), Backoff.Length - 1)];

    public Task<int> CountAsync() => _store.CountOutboxAsync();

    public Task<long> EnqueueAsync<T>(
        Guid churchId,
        HttpMethod method,
        string path,
        T body,
        JsonTypeInfo<T> info,
        string label,
        StoreEntity? entity) =>
        _store.EnqueueAsync(churchId, method.Method, path, JsonSerializer.Serialize(body, info), label, entity, _now());

    public Task<long> EnqueueAsync(Guid churchId, HttpMethod method, string path, string label, StoreEntity? entity) =>
        _store.EnqueueAsync(churchId, method.Method, path, null, label, entity, _now());

    /// <summary>
    /// Sends the queue in order, one request at a time. Success removes the entry; a network error, 5xx, 429 or 401 stops
    /// the flush (the entry stays); any other 4xx is final: the entry is dropped and reported through <see cref="Discarded"/>.
    /// Only one flush runs at a time.
    /// </summary>
    public async Task<FlushOutcome> FlushAsync(CancellationToken ct = default)
    {
        await _flushGate.WaitAsync(ct);
        try
        {
            while (await _store.PeekOutboxAsync() is { } entry)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await _api.SendAsync(new ApiRequest(new HttpMethod(entry.Method), entry.Path, entry.Body), ct);
                    await _store.DeleteOutboxAsync(entry.Seq);
                    _consecutiveFailures = 0;
                    _log.LogInformation("Enviado {Method} {Path}", entry.Method, entry.Path);
                }
                catch (NetworkException ex)
                {
                    return await Block(entry, ex.Message);
                }
                catch (ApiException ex) when (ex.IsServerError || ex.StatusCode is 429 or 401)
                {
                    return await Block(entry, ex.Message);
                }
                catch (ApiException ex) when (ex.StatusCode == 404 && entry.Method == "DELETE")
                {
                    // Already gone (a retry after a lost answer, or deleted elsewhere): the goal is reached.
                    await _store.DeleteOutboxAsync(entry.Seq);
                    _consecutiveFailures = 0;
                }
                catch (ApiException ex)
                {
                    _log.LogWarning("Descartado {Method} {Path}: {Status} {Code}", entry.Method, entry.Path, ex.StatusCode, ex.Code);
                    await _store.DeleteOutboxAsync(entry.Seq);
                    Discarded?.Invoke(this, new DiscardedWrite(entry.Label, ex.Message, ex.StatusCode, entry.Entity));
                }
            }

            _consecutiveFailures = 0;
            return FlushOutcome.Done;
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task<FlushOutcome> Block(OutboxEntry entry, string reason)
    {
        _consecutiveFailures++;
        await _store.RecordAttemptAsync(entry.Seq, reason);
        _log.LogInformation("Cola detenida en {Method} {Path}: {Reason}", entry.Method, entry.Path, reason);
        return FlushOutcome.Blocked;
    }
}
