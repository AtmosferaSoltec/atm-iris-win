using System.Text.Json;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Networking.Fake;
using Iris.Core.Persistence;
using Iris.Core.Services.Live;
using Iris.Core.Sync;
using Iris.Core.Timing;

namespace Iris.Tests;

public class TimeRecordTests
{
    private static async Task<SyncTestStack> ReadyAsync(string email = "pastor@vidanueva.org")
    {
        var s = new SyncTestStack(email);
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        return s;
    }

    private static ServiceRecord NewRecord(DateTime date, Guid typeId, Guid? personId, string? personName) => new(
        Guid.NewGuid(),
        date,
        typeId,
        [
            new BlockRecord(Guid.NewGuid(), "Bienvenida", 600, 640, personId, personName, BlockStatus.Completed),
            new BlockRecord(Guid.NewGuid(), "Anuncios", 300, 0, null, null, BlockStatus.Skipped),
        ],
        "Culto general");

    private static List<ServiceRecordDto> ServerRecords(SyncTestStack s) =>
        FakeRows.Live(s.Stack.Fake.Db, s.ChurchId, FakeKind.ServiceRecords, IrisJsonContext.Default.ServiceRecordDto).ToList();

    [Fact]
    public async Task A_service_finished_offline_reaches_the_server_exactly_once()
    {
        using var s = await ReadyAsync();
        var records = new LiveTimeRecordRepository(s.Data);
        var daniel = Iris.Core.Services.Mocks.MockChurchData.DanielRuiz;
        var record = NewRecord(new DateTime(2026, 10, 4, 10, 0, 0), Iris.Core.Services.Mocks.MockChurchData.CultoGeneral.Id, daniel.Id, daniel.Name);
        s.Connectivity.IsOnline = false;

        await records.SaveAsync(record);
        await s.Engine.SyncNowAsync(SyncReason.Write);

        Assert.True(await records.IsPendingAsync(record.Id));
        Assert.DoesNotContain(ServerRecords(s), r => r.Id == record.Id);
        Assert.Contains(await records.RecordsAsync(), r => r.Id == record.Id);

        s.Connectivity.IsOnline = true;
        await s.Engine.SyncNowAsync(SyncReason.Reconnected);
        // A retry of the same save (what the "Reintentar" button does) does not create another one.
        await records.SaveAsync(record);
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.Single(ServerRecords(s), r => r.Id == record.Id);
        Assert.False(await records.IsPendingAsync(record.Id));
        var saved = ServerRecords(s).Single(r => r.Id == record.Id);
        Assert.Equal("Culto general", saved.ServiceTypeName);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.Zero), saved.Date); // 10:00 in Lima = 15:00 UTC
        Assert.Equal("skipped", saved.Blocks[1].Status);
    }

    [Fact]
    public async Task Adjusting_a_duration_and_changing_a_leader_are_sent_as_block_patches()
    {
        using var s = await ReadyAsync();
        var records = new LiveTimeRecordRepository(s.Data);
        var people = new LivePeopleRepository(s.Data);
        var existing = (await records.RecordsAsync())[0];
        var block = existing.Blocks.First(b => b.Status == BlockStatus.Completed);
        var other = (await people.PeopleAsync()).First(p => p.Id != block.PersonId);

        var edited = existing with
        {
            Blocks = existing.Blocks
                .Select(b => b.Id == block.Id ? b with { ActualSeconds = 1234, Status = BlockStatus.Adjusted, PersonId = other.Id, PersonName = other.Name } : b)
                .ToList(),
        };
        await records.SaveAsync(edited);
        s.Stack.Fake.Calls.Clear();
        await s.Engine.SyncNowAsync(SyncReason.Write);

        var writes = s.Stack.Fake.Calls.Where(c => c.StartsWith("PATCH")).ToList();
        Assert.Equal(2, writes.Count);
        var server = ServerRecords(s).Single(r => r.Id == existing.Id).Blocks.Single(b => b.Id == block.Id);
        Assert.Equal(1234, server.ActualSeconds);
        Assert.Equal("adjusted", server.Status);
        Assert.Equal(other.Id, server.PersonId);
        Assert.Equal(other.Name, server.PersonName);

        var local = (await records.RecordsAsync()).Single(r => r.Id == existing.Id).Blocks.Single(b => b.Id == block.Id);
        Assert.Equal(BlockStatus.Adjusted, local.Status);
        Assert.Equal(1234, local.ActualSeconds);
    }

    [Fact]
    public async Task Clearing_the_leader_sends_an_explicit_null()
    {
        using var s = await ReadyAsync();
        var records = new LiveTimeRecordRepository(s.Data);
        var existing = (await records.RecordsAsync())[0];
        var block = existing.Blocks.First(b => b.PersonId is not null);

        await records.SaveAsync(existing with { Blocks = existing.Blocks.Select(b => b.Id == block.Id ? b with { PersonId = null, PersonName = null } : b).ToList() });
        await s.Engine.SyncNowAsync(SyncReason.Write);

        var server = ServerRecords(s).Single(r => r.Id == existing.Id).Blocks.Single(b => b.Id == block.Id);
        Assert.Null(server.PersonId);
        Assert.Null(server.PersonName);
    }

    [Fact]
    public async Task Deleting_a_record_sends_a_delete_and_a_repeated_delete_is_harmless()
    {
        using var s = await ReadyAsync();
        var records = new LiveTimeRecordRepository(s.Data);
        var victim = (await records.RecordsAsync())[0];

        await records.DeleteAsync(victim.Id);
        await s.Engine.SyncNowAsync(SyncReason.Write);
        Assert.DoesNotContain(ServerRecords(s), r => r.Id == victim.Id);

        // Queue the same delete again: the server answers 404 and the outbox treats it as done.
        await s.Data.QueueAsync(HttpMethod.Delete, $"service-records/{victim.Id}", "otra vez", StoreEntity.ServiceRecords);
        var discarded = 0;
        s.Engine.WriteDiscarded += (_, _) => discarded++;
        await s.Engine.SyncNowAsync(SyncReason.Write);

        Assert.Equal(0, discarded);
        Assert.Equal(0, await s.Outbox.CountAsync());
    }

    [Fact]
    public async Task Operator_can_save_records_but_not_adjust_them()
    {
        using var s = await ReadyAsync("operador@vidanueva.org");
        var records = new LiveTimeRecordRepository(s.Data);
        var typeId = (await new LiveServiceTypeRepository(s.Data).ServiceTypesAsync()).First().Id;
        var created = NewRecord(new DateTime(2026, 10, 4, 10, 0, 0), typeId, null, null);
        await records.SaveAsync(created);
        await s.Engine.SyncNowAsync(SyncReason.Write);
        Assert.Single(ServerRecords(s), r => r.Id == created.Id);

        var discarded = new List<DiscardedWrite>();
        s.Engine.WriteDiscarded += (_, d) => discarded.Add(d);
        await records.SaveAsync(created with { Blocks = created.Blocks.Select(b => b with { ActualSeconds = b.ActualSeconds + 60 }).ToList() });
        await s.Engine.SyncNowAsync(SyncReason.Write);
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.NotEmpty(discarded);
        Assert.All(discarded, d => Assert.Equal(403, d.StatusCode));
        // The copy heals back to what the server holds.
        var local = (await records.RecordsAsync()).Single(r => r.Id == created.Id);
        Assert.Equal(640, local.Blocks[0].ActualSeconds);
    }

    [Fact]
    public async Task A_record_at_23_30_on_the_last_day_of_the_month_belongs_to_that_month_in_the_church_zone()
    {
        using var s = await ReadyAsync();
        var session = s.Session.Current!;
        // 04:30 UTC on 1 October is 23:30 on 30 September in Lima.
        var dto = new ServiceRecordDto(Guid.NewGuid(), new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero), Guid.NewGuid(), "Culto", [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var model = Mapping.ToModel(dto, session.Church.TimeZone);

        Assert.Equal(new DateTime(2026, 9, 30, 23, 30, 0), model.Date);
        Assert.Equal(9, model.Date.Month);
    }

    [Fact]
    public async Task Person_block_counts_follow_the_records_on_the_server()
    {
        using var s = await ReadyAsync();
        var records = new LiveTimeRecordRepository(s.Data);
        var people = new LivePeopleRepository(s.Data);
        var ana = (await people.PeopleAsync()).Single(p => p.Name == "Ana Torres");
        var before = ana.BlockCount;
        var typeId = (await new LiveServiceTypeRepository(s.Data).ServiceTypesAsync()).First().Id;

        await records.SaveAsync(NewRecord(new DateTime(2026, 10, 4, 10, 0, 0), typeId, ana.Id, ana.Name));
        await s.Engine.SyncNowAsync(SyncReason.Write);
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.Equal(before + 1, (await people.PeopleAsync()).Single(p => p.Id == ana.Id).BlockCount);
    }

    [Fact]
    public void Timer_record_carries_the_service_type_name_and_a_stable_id()
    {
        var timer = new BlockTimer([new BlockTemplate(Guid.NewGuid(), "Bienvenida", 10, null)]);
        timer.Start(null, new DateTime(2026, 10, 4, 10, 0, 0));
        timer.Finish(new DateTime(2026, 10, 4, 10, 12, 0));

        var record = timer.Record(Guid.NewGuid(), new DateTime(2026, 10, 4, 10, 0, 0), new Dictionary<Guid, string>(), "Culto general");

        Assert.Equal("Culto general", record.ServiceTypeName);
        Assert.Equal(720, record.Blocks[0].ActualSeconds);
        Assert.NotEqual(Guid.Empty, record.Id);
    }
}
