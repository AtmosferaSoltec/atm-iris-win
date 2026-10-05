using System.Text.Json;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Services.Live;
using Iris.Core.Sync;

namespace Iris.Tests;

public class SongTests
{
    private static async Task<SyncTestStack> ReadyAsync()
    {
        var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        return s;
    }

    private static async Task<JsonElement> GetAsync(SyncTestStack s, string path)
    {
        var response = await s.Stack.Fake.SendRawAsync(HttpMethod.Get, path, bearer: s.Stack.Auth.CurrentAccessToken);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Library_lists_songs_ordered_by_title_name_key()
    {
        using var s = await ReadyAsync();
        var library = new LiveLibraryRepository(s.Data, new Iris.Core.Media.NullMediaCache());

        var titles = (await library.LyricsAsync()).Select(l => l.Title).ToList();

        Assert.Equal(6, titles.Count);
        Assert.Equal(titles.OrderBy(t => Iris.Core.Formatting.NameKey.For(t), StringComparer.Ordinal).ToList(), titles);
    }

    [Fact]
    public async Task Songs_list_is_paginated_and_search_ignores_accents_and_case()
    {
        using var s = await ReadyAsync();

        var page = await GetAsync(s, "songs?limit=2&page=2");
        Assert.Equal(2, page.GetProperty("data").GetArrayLength());
        Assert.Equal(6, page.GetProperty("meta").GetProperty("total").GetInt32());
        Assert.Equal(3, page.GetProperty("meta").GetProperty("totalPages").GetInt32());

        var byTitle = await GetAsync(s, "songs?search=" + Uri.EscapeDataString("CARIÑOSO"));
        Assert.Contains("Cariñoso Salvador", byTitle.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("title").GetString()));

        // "gracia" is in the title of one song and in the lyrics of others: the title hit comes first.
        var relevance = await GetAsync(s, "songs?search=gracia");
        Assert.Equal("Sublime gracia", relevance.GetProperty("data")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Search_in_the_lyrics_text_finds_songs_in_the_add_to_service_sheet_key()
    {
        using var s = await ReadyAsync();
        var library = new LiveLibraryRepository(s.Data, new Iris.Core.Media.NullMediaCache());

        var sheet = (await library.LyricsAsync()).Single(l => l.Title == "Sublime gracia");
        var text = string.Join(' ', sheet.Sections.Select(x => ((TextContent)x.Content).Body));

        Assert.Contains("ciego", Iris.Core.Formatting.NameKey.For(text));
    }

    [Fact]
    public async Task A_song_added_on_the_server_arrives_with_the_next_sync()
    {
        using var s = await ReadyAsync();
        var library = new LiveLibraryRepository(s.Data, new Iris.Core.Media.NullMediaCache());
        var before = (await library.LyricsAsync()).Count;

        var added = s.Stack.Fake.AddTestSong(s.ChurchId);
        Assert.Equal(before, (await library.LyricsAsync()).Count);
        await s.Engine.SyncNowAsync(SyncReason.Manual);

        var after = await library.LyricsAsync();
        Assert.Equal(before + 1, after.Count);
        Assert.Equal("Dominio público", after.Single(l => l.Id == added.Id).Copyright);
    }

    [Fact]
    public async Task Operator_cannot_write_songs_and_import_skips_duplicates()
    {
        using var s = await ReadyAsync();
        var body = "{\"songs\":[{\"title\":\"sublime GRACIA\",\"author\":\"x\",\"copyright\":null,\"sections\":[{\"label\":null,\"text\":\"a\"}]},{\"title\":\"Nueva\",\"author\":\"\",\"copyright\":null,\"sections\":[{\"label\":null,\"text\":\"b\"}]}]}";
        var response = await s.Stack.Fake.SendRawAsync(HttpMethod.Post, "songs/import", body, s.Stack.Auth.CurrentAccessToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("created").GetArrayLength());
        Assert.Equal("duplicate", data.GetProperty("skipped")[0].GetProperty("reason").GetString());

        using var op = new SyncTestStack("operador@vidanueva.org");
        await op.SignInAsync();
        var denied = await op.Stack.Fake.SendRawAsync(HttpMethod.Post, "songs/import", body, op.Stack.Auth.CurrentAccessToken);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
