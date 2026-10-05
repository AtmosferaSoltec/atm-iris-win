using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Persistence;
using Iris.Core.Sync;

namespace Iris.Tests;

public class SyncEngineTests
{
    [Fact]
    public async Task First_sync_fills_the_local_copy()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();

        await s.Engine.SyncNowAsync(SyncReason.SignedIn);

        Assert.Equal(SyncPhase.Idle, s.Engine.Status.Phase);
        Assert.Equal(8, (await s.Store.GetPeopleAsync()).Count);
        Assert.Equal(3, (await s.Store.GetServiceTypesAsync()).Count);
        Assert.Equal(6, (await s.Store.GetSongsAsync()).Count);
        Assert.Equal(10, (await s.Store.GetServiceRecordsAsync()).Count);
        Assert.NotNull(await s.Store.GetChurchAsync());
        Assert.True((await s.Store.GetSyncStateAsync()).InitialDone);
    }

    [Fact]
    public async Task Pages_are_applied_in_order_until_there_is_no_more()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        s.Stack.Fake.Calls.Clear();

        await s.Engine.SyncNowAsync(SyncReason.SignedIn);

        // 1 church + 8 + 3 + 10 + 6 rows = 28 entities at 200 per page: one page.
        Assert.Single(s.Stack.Fake.Calls, c => c == "GET /sync/changes");
        var state = await s.Store.GetSyncStateAsync();
        Assert.NotEqual("0", state.Cursor);
    }

    [Fact]
    public async Task Second_sync_with_no_changes_applies_nothing()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        var completed = 0;
        s.Engine.Synced += (_, _) => completed++;

        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.Equal(0, completed);
    }

    [Fact]
    public async Task Server_changes_and_deletes_arrive_incrementally()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);

        var people = await s.Store.GetPeopleAsync();
        var gone = people[0];
        FakeEdit.DeletePerson(s.Stack.Fake, s.ChurchId, gone.Id);
        FakeEdit.AddPerson(s.Stack.Fake, s.ChurchId, "Nuevo Nombre");
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        var after = await s.Store.GetPeopleAsync();
        Assert.DoesNotContain(after, p => p.Id == gone.Id);
        Assert.Contains(after, p => p.Name == "Nuevo Nombre");
        Assert.Equal(8, after.Count);
    }

    [Fact]
    public async Task Offline_sync_reports_offline_and_keeps_data()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        s.Connectivity.IsOnline = false;

        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.Equal(SyncPhase.Offline, s.Engine.Status.Phase);
        Assert.Equal(8, (await s.Store.GetPeopleAsync()).Count);
    }

    [Fact]
    public async Task Request_failing_while_online_hint_is_true_also_reports_offline()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        s.Stack.Offline = true;

        await s.Engine.SyncNowAsync(SyncReason.SignedIn);

        Assert.Equal(SyncPhase.Offline, s.Engine.Status.Phase);
        Assert.True(s.Engine.Status.IsFirstSync);
    }

    [Fact]
    public async Task Pull_is_paused_while_suspended_and_runs_on_resume()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        s.Engine.Suspend();

        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        Assert.Empty(await s.Store.GetPeopleAsync());

        s.Engine.Resume();
        await Eventually(async () => (await s.Store.GetPeopleAsync()).Count == 8);
    }

    [Fact]
    public async Task Switching_church_wipes_the_copy()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);

        await s.Engine.BindChurchAsync(Guid.NewGuid());

        Assert.Empty(await s.Store.GetPeopleAsync());
        Assert.False((await s.Store.GetSyncStateAsync()).InitialDone);
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(await condition());
    }
}

/// <summary>Edits the fake API's state as if another device had written to the server.</summary>
internal static class FakeEdit
{
    public static void AddPerson(Iris.Core.Networking.Fake.FakeIrisApiHandler fake, Guid churchId, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        Iris.Core.Networking.Fake.FakeRows.Put(fake.Db, churchId, Iris.Core.Networking.Fake.FakeKind.People, id, new PersonDto(id, name, 0, now, now), IrisJsonContext.Default.PersonDto);
    }

    public static void DeletePerson(Iris.Core.Networking.Fake.FakeIrisApiHandler fake, Guid churchId, Guid id) =>
        Iris.Core.Networking.Fake.FakeRows.SoftDelete(fake.Db, churchId, Iris.Core.Networking.Fake.FakeKind.People, id);
}
