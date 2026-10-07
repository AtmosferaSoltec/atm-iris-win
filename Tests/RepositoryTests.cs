using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Networking.Fake;
using Iris.Core.Persistence;
using Iris.Core.Services.Live;
using Iris.Core.Sync;

namespace Iris.Tests;

public class RepositoryTests
{
    private static async Task<SyncTestStack> ReadyAsync(string email = "pastor@vidanueva.org")
    {
        var s = new SyncTestStack(email);
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        return s;
    }

    private static List<PersonDto> ServerPeople(SyncTestStack s) =>
        FakeRows.Live(s.Stack.Fake.Db, s.ChurchId, FakeKind.People, IrisJsonContext.Default.PersonDto).ToList();

    [Fact]
    public async Task Person_added_offline_reaches_the_server_exactly_once_on_reconnect()
    {
        using var s = await ReadyAsync();
        var people = new LivePeopleRepository(s.Data);
        s.Connectivity.IsOnline = false;

        var added = await people.AddAsync("  Nuevo   Líder ");
        await s.Engine.SyncNowAsync(SyncReason.Write);

        Assert.Contains(await people.PeopleAsync(), p => p.Id == added.Id);
        Assert.DoesNotContain(ServerPeople(s), p => p.Id == added.Id);
        Assert.Equal(1, await s.Outbox.CountAsync());

        s.Connectivity.IsOnline = true;
        await s.Engine.SyncNowAsync(SyncReason.Reconnected);
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.Single(ServerPeople(s), p => p.Id == added.Id);
        Assert.Equal(0, await s.Outbox.CountAsync());
        Assert.Single(await people.PeopleAsync(), p => p.Id == added.Id);
    }

    [Fact]
    public async Task Resending_a_create_after_a_lost_answer_does_not_duplicate()
    {
        using var s = await ReadyAsync();
        var people = new LivePeopleRepository(s.Data);
        var added = await people.AddAsync("Ana María");
        await s.Engine.SyncNowAsync(SyncReason.Write);

        // The same request again (what a retry after a lost answer looks like).
        var body = $"{{\"id\":\"{added.Id}\",\"name\":\"Ana María\"}}";
        var token = s.Stack.Auth.CurrentAccessToken;
        var response = await s.Stack.Fake.SendRawAsync(HttpMethod.Post, "people", body, token);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Single(ServerPeople(s), p => p.Name == "Ana María");
    }

    [Fact]
    public async Task Duplicate_names_are_rejected_by_name_key()
    {
        using var s = await ReadyAsync();
        var people = new LivePeopleRepository(s.Data);

        await Assert.ThrowsAsync<InvalidOperationException>(() => people.AddAsync("  daniel   RUIZ "));

        await people.AddAsync("Elías Soto");
        await Assert.ThrowsAsync<InvalidOperationException>(() => people.AddAsync("elias soto"));
    }

    [Fact]
    public async Task Server_rejects_duplicate_person_with_PERSON_NAME_TAKEN()
    {
        using var s = await ReadyAsync();
        var token = s.Stack.Auth.CurrentAccessToken;

        var response = await s.Stack.Fake.SendRawAsync(HttpMethod.Post, "people", "{\"name\":\"lucia gomez\"}", token);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("PERSON_NAME_TAKEN", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Queued_writes_run_in_order()
    {
        using var s = await ReadyAsync();
        var people = new LivePeopleRepository(s.Data);
        s.Connectivity.IsOnline = false;
        var p = await people.AddAsync("Primero");
        await people.RenameAsync(p.Id, "Segundo");
        s.Stack.Fake.Calls.Clear();

        s.Connectivity.IsOnline = true;
        await s.Engine.SyncNowAsync(SyncReason.Reconnected);

        var writes = s.Stack.Fake.Calls.Where(c => !c.StartsWith("GET")).ToList();
        Assert.Equal(["POST /people", $"PATCH /people/{p.Id}"], writes);
        Assert.Equal("Segundo", ServerPeople(s).Single(x => x.Id == p.Id).Name);
    }

    [Fact]
    public async Task Deleting_a_person_removes_them_locally_and_on_the_server()
    {
        using var s = await ReadyAsync();
        var people = new LivePeopleRepository(s.Data);
        var daniel = (await people.PeopleAsync()).Single(p => p.Name == "Daniel Ruiz");

        await people.DeleteAsync(daniel.Id);
        Assert.DoesNotContain(await people.PeopleAsync(), p => p.Id == daniel.Id);

        await s.Engine.SyncNowAsync(SyncReason.Write);
        Assert.DoesNotContain(ServerPeople(s), p => p.Id == daniel.Id);
    }

    [Fact]
    public async Task Service_type_save_is_an_idempotent_PUT_and_name_conflicts_are_reported()
    {
        using var s = await ReadyAsync();
        var types = new LiveServiceTypeRepository(s.Data);
        var created = new ServiceType(Guid.NewGuid(), "Oración", "#3DDC97", new ServiceSchedule(4, 19, 30), [new BlockTemplate(Guid.NewGuid(), "Intercesión", 20)]);
        await types.SaveAsync(created);
        await s.Engine.SyncNowAsync(SyncReason.Write);
        await types.SaveAsync(created with { Color = "#FF7A59" });
        await s.Engine.SyncNowAsync(SyncReason.Write);

        var server = FakeRows.Live(s.Stack.Fake.Db, s.ChurchId, FakeKind.ServiceTypes, IrisJsonContext.Default.ServiceTypeDto).Where(t => t.Id == created.Id).ToList();
        Assert.Single(server);
        Assert.Equal("#FF7A59", server[0].Color);

        await Assert.ThrowsAsync<InvalidOperationException>(() => types.SaveAsync(created with { Id = Guid.NewGuid(), Name = "oracion" }));
    }

    [Fact]
    public async Task A_write_the_server_refuses_is_dropped_reported_and_healed()
    {
        using var s = await ReadyAsync();
        var types = new LiveServiceTypeRepository(s.Data);
        var discarded = new List<DiscardedWrite>();
        s.Engine.WriteDiscarded += (_, d) => discarded.Add(d);

        // A name over 60 characters: the copy takes it, the server answers 400 to the queued PUT.
        var type = new ServiceType(Guid.NewGuid(), new string('x', 61), "#FFB547", null, []);
        await types.SaveAsync(type);
        await s.Engine.SyncNowAsync(SyncReason.Write);
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        Assert.Single(discarded);
        Assert.Equal(400, discarded[0].StatusCode);
        Assert.Equal(0, await s.Outbox.CountAsync());
        Assert.DoesNotContain(await types.ServiceTypesAsync(), t => t.Id == type.Id);
    }

    [Fact]
    public async Task Modules_are_read_from_the_copy_and_saved_through_the_queue()
    {
        using var s = await ReadyAsync();
        var modules = new LiveModuleSettingsRepository(s.Data);

        // The Bible is off for all of Iris (api-contract §6): it comes off and is not offered.
        Assert.Equal(new ChurchModules(false, true, true), await modules.ModulesAsync());
        Assert.Equal(new ChurchModules(false, true, true), await modules.AvailableModulesAsync());

        await modules.SaveAsync(new ChurchModules(false, true, false));
        await s.Engine.SyncNowAsync(SyncReason.Write);

        Assert.Equal(new ChurchModules(false, true, false), await modules.ModulesAsync());
        var server = s.Stack.Fake.Db.Churches.Single(c => c.Id == s.ChurchId);
        Assert.True(server.Bible); // the church's own choice is kept for when the Bible comes back
        Assert.False(server.TimeControl);
    }


    [Fact]
    public async Task People_sort_in_spanish_order()
    {
        using var s = await ReadyAsync();
        var people = new LivePeopleRepository(s.Data);
        await people.AddAsync("Álvaro Díaz");
        await people.AddAsync("zacarías Peña");
        await people.AddAsync("Ángela Ruiz");

        var names = (await people.PeopleAsync()).Select(p => p.Name).ToList();

        Assert.Equal(names.OrderBy(n => n, Iris.Core.Formatting.Spanish.Comparer).ToList(), names);
        Assert.Equal("zacarías Peña", names[^1]);
        Assert.True(names.IndexOf("Álvaro Díaz") < names.IndexOf("Carlos Pérez"));
    }

    [Fact]
    public async Task Projection_settings_are_saved_through_the_queue_and_come_back_with_sync()
    {
        using var s = await ReadyAsync();
        var projection = new LiveProjectionSettingsRepository(s.Data);
        Assert.Equal(ProjectionSettings.Default, await projection.SettingsAsync());

        var chosen = new ProjectionSettings(ProjectionFontFamily.Georgia, 112, "aurora");
        await projection.SaveAsync(chosen);
        Assert.Equal(chosen, await projection.SettingsAsync());
        await s.Engine.SyncNowAsync(SyncReason.Write);

        var server = s.Stack.Fake.Db.Churches.Single(c => c.Id == s.ChurchId);
        Assert.Equal("georgia", server.ProjectionFontFamily);
        Assert.Equal(112, server.ProjectionFontSizePt);
        Assert.Equal("aurora", server.ProjectionDefaultBackgroundId);

        // Another console changed it: the next sync brings it here.
        server.ProjectionFontSizePt = 64;
        server.Version = s.Stack.Fake.Db.NextVersion();
        await s.Engine.SyncNowAsync(SyncReason.Manual);
        Assert.Equal(64, (await projection.SettingsAsync()).FontSizePt);
    }

    [Fact]
    public async Task An_unknown_font_key_falls_back_to_the_recommended_one()
    {
        Assert.Equal(ProjectionFontFamily.System, Mapping.ParseFontFamily("comicSans"));
        Assert.Equal("avenirNext", Mapping.ToWire(ProjectionFontFamily.AvenirNext));

        using var s = await ReadyAsync();
        var bad = await s.Stack.Fake.SendRawAsync(HttpMethod.Put, "church/projection", "{\"fontFamily\":\"system\",\"fontSizePt\":300,\"defaultBackgroundId\":null}", s.Stack.Auth.CurrentAccessToken);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task The_church_reports_storage_by_section_and_media_filters_by_several_kinds()
    {
        using var s = await ReadyAsync();
        var token = s.Stack.Auth.CurrentAccessToken;

        var church = await s.Stack.Fake.SendRawAsync(HttpMethod.Get, "church", null, token);
        var storage = System.Text.Json.JsonDocument.Parse(await church.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("storage");
        var breakdown = storage.GetProperty("breakdown");
        var music = breakdown.GetProperty("musicBytes").GetInt64();
        var backgrounds = breakdown.GetProperty("backgroundBytes").GetInt64();
        var media = breakdown.GetProperty("mediaBytes").GetInt64();
        Assert.True(music > 0 && backgrounds > 0 && media > 0);
        Assert.Equal(music + backgrounds + media, storage.GetProperty("usedBytes").GetInt64());

        var list = await s.Stack.Fake.SendRawAsync(HttpMethod.Get, "media?kind=image,video", null, token);
        var items = System.Text.Json.JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.All(items.EnumerateArray(), m => Assert.NotEqual("audio", m.GetProperty("kind").GetString()));
        Assert.Equal(2, items.GetArrayLength());

        var wrong = await s.Stack.Fake.SendRawAsync(HttpMethod.Get, "media?kind=image,gif", null, token);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, wrong.StatusCode);
    }
}
