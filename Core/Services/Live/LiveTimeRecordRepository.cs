using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Persistence;

namespace Iris.Core.Services.Live;

/// <summary>
/// Time records of the whole church, from the local copy. Saving a new record queues <c>PUT /service-records/:id</c>
/// (the id is made by the block timer and stays the same on every retry). Saving a record that already exists is an
/// adjustment: each block whose time or leader changed becomes its own <c>PATCH …/blocks/:blockId</c>, which is what the
/// server marks as "adjusted". Deleting queues <c>DELETE</c>.
/// </summary>
public sealed class LiveTimeRecordRepository(LiveData data) : ITimeRecordRepository
{
    public async Task<IReadOnlyList<ServiceRecord>> RecordsAsync()
    {
        var zone = data.Zone;
        return (await data.Store.GetServiceRecordsAsync())
            .Select(r => Mapping.ToModel(r, zone))
            .OrderByDescending(r => r.Date)
            .ToList();
    }

    public async Task SaveAsync(ServiceRecord record)
    {
        var zone = data.Zone;
        var now = data.Now;
        var existing = (await data.Store.GetServiceRecordsAsync()).FirstOrDefault(r => r.Id == record.Id);
        var label = string.IsNullOrWhiteSpace(record.ServiceTypeName) ? "Registro de tiempos" : $"Tiempos de {record.ServiceTypeName}";
        if (existing is null)
        {
            await data.Store.UpsertAsync(Mapping.ToDto(record, zone, now, now));
            await data.QueueAsync(HttpMethod.Put, $"service-records/{record.Id}", Mapping.ToInput(record, zone), IrisJsonContext.Default.ServiceRecordInputDto, label, StoreEntity.ServiceRecords);
            return;
        }

        var blocks = existing.Blocks.ToList();
        var patches = new List<(Guid BlockId, string BlockName, bool Actual, BlockRecordDto Updated)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var before = blocks[i];
            var after = record.Blocks.FirstOrDefault(b => b.Id == before.Id);
            if (after is null)
            {
                continue;
            }

            var actualChanged = after.ActualSeconds != before.ActualSeconds;
            var personChanged = after.PersonId != before.PersonId;
            if (!actualChanged && !personChanged)
            {
                continue;
            }

            blocks[i] = before with
            {
                ActualSeconds = after.ActualSeconds,
                PersonId = after.PersonId,
                PersonName = after.PersonId is null ? null : after.PersonName,
                Status = actualChanged ? "adjusted" : before.Status,
            };
            patches.Add((before.Id, before.Name, actualChanged, blocks[i]));
            if (actualChanged && personChanged)
            {
                patches.Add((before.Id, before.Name, false, blocks[i]));
            }
        }

        if (patches.Count == 0)
        {
            return;
        }

        await data.Store.UpsertAsync(existing with { Blocks = blocks, UpdatedAt = now });
        foreach (var (blockId, blockName, actual, updated) in patches)
        {
            if (actual)
            {
                await data.QueueAsync(HttpMethod.Patch, $"service-records/{record.Id}/blocks/{blockId}", new BlockActualPatchDto(updated.ActualSeconds), IrisJsonContext.Default.BlockActualPatchDto, $"{label} · {blockName}", StoreEntity.ServiceRecords);
            }
            else
            {
                await data.QueueAsync(HttpMethod.Patch, $"service-records/{record.Id}/blocks/{blockId}", new BlockPersonPatchDto(updated.PersonId), IrisJsonContext.Default.BlockPersonPatchDto, $"{label} · {blockName}", StoreEntity.ServiceRecords);
            }
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var existing = (await data.Store.GetServiceRecordsAsync()).FirstOrDefault(r => r.Id == id);
        await data.Store.DeleteAsync(StoreEntity.ServiceRecords, id);
        await data.QueueAsync(HttpMethod.Delete, $"service-records/{id}", existing is null ? "Registro de tiempos" : $"Tiempos de {existing.ServiceTypeName}", StoreEntity.ServiceRecords);
    }

    /// <summary>True while the record's creation (or an adjustment) still waits in the outbox: "Se enviará cuando haya conexión".</summary>
    public async Task<bool> IsPendingAsync(Guid id) =>
        (await data.Store.GetOutboxAsync()).Any(e => e.Path.StartsWith($"service-records/{id}", StringComparison.Ordinal));
}
