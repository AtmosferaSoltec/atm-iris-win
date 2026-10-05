using System;
using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Iris.Core.Persistence;
using Iris.Core.Sync;

namespace Iris.Core.Services.Live;

/// <summary>
/// What every <c>Live…</c> repository shares: the local copy to read and write, the church and its time zone, and the
/// "write locally, then queue" step that ends with a nudge to the sync engine.
/// </summary>
public sealed class LiveData(LocalStore store, Outbox outbox, ISyncService sync, ISessionContext session)
{
    public LocalStore Store { get; } = store;

    public Guid ChurchId => session.Current?.Church.Id ?? throw new InvalidOperationException("No hay una sesión iniciada.");

    public TimeZoneInfo Zone => session.Current?.Church.TimeZone ?? TimeZoneInfo.Local;

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <summary>Queues a write with a JSON body (after the local change was applied) and asks the engine to send it.</summary>
    public async Task QueueAsync<T>(HttpMethod method, string path, T body, JsonTypeInfo<T> info, string label, StoreEntity? entity)
    {
        await outbox.EnqueueAsync(ChurchId, method, path, body, info, label, entity);
        Nudge();
    }

    /// <summary>Queues a write with no body (delete).</summary>
    public async Task QueueAsync(HttpMethod method, string path, string label, StoreEntity? entity)
    {
        await outbox.EnqueueAsync(ChurchId, method, path, label, entity);
        Nudge();
    }

    private void Nudge() => _ = sync.SyncNowAsync(SyncReason.Write);
}
