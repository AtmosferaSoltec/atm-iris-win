using Iris.Core.Bible;
using Iris.Core.Models;
using Iris.Core.Services;
using Iris.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iris.Tests;

public sealed class BibleStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iris-bible-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private BibleStore NewStore(TestStack stack) => new(stack.Api, _root, NullLogger<BibleStore>.Instance, () => stack.Now);

    /// <summary>The Bible is switched off for all of Iris today (api-contract §6): these tests switch it on.</summary>
    private static async Task<TestStack> SignedInAsync()
    {
        var stack = new TestStack();
        stack.Fake.Db.SystemBibleEnabled = true;
        await stack.SignInAsync();
        return stack;
    }

    [Fact]
    public async Task With_the_bible_switched_off_for_all_of_iris_it_is_hidden_and_not_downloaded()
    {
        using var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);

        var modules = new Iris.Core.Services.Live.LiveModuleSettingsRepository(s.Data);
        Assert.False((await modules.AvailableModulesAsync()).Bible);
        Assert.False((await modules.ModulesAsync()).Bible);

        var response = await s.Stack.Fake.SendRawAsync(HttpMethod.Get, "bible/translations", null, s.Stack.Auth.CurrentAccessToken);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_makes_the_whole_bible_available()
    {
        var stack = await SignedInAsync();
        var store = NewStore(stack);

        await store.EnsureAsync();

        Assert.Equal(BiblePhase.Ready, store.Status.Phase);
        Assert.Equal("Reina-Valera 1909", store.TranslationName);
        var books = await store.BooksAsync();
        Assert.Equal(66, books.Count);
        Assert.Equal("GEN", books[0].Id);
        Assert.Equal(Testament.New, books.Single(b => b.Id == "JHN").Testament);
        Assert.Equal(36, store.VerseCount("JHN", 3));
        Assert.StartsWith("Porque de tal manera amó Dios al mundo", store.Verses("JHN", 3).Single(v => v.Number == 16).Text);
        Assert.Equal(150, books.Single(b => b.Id == "PSA").ChapterCount);
    }

    [Fact]
    public async Task First_download_without_connection_asks_to_retry_and_then_works()
    {
        var stack = await SignedInAsync();
        var store = NewStore(stack);
        stack.Offline = true;

        await store.EnsureAsync();

        Assert.Equal(BiblePhase.Failed, store.Status.Phase);
        Assert.Equal("Necesitas conexión para descargar la Biblia la primera vez.", store.Status.Message);

        stack.Offline = false;
        await store.EnsureAsync(force: true);
        Assert.Equal(BiblePhase.Ready, store.Status.Phase);
    }

    [Fact]
    public async Task A_restart_reads_the_file_from_disk_without_network()
    {
        var stack = await SignedInAsync();
        await NewStore(stack).EnsureAsync();
        stack.Offline = true;

        var reopened = NewStore(stack);
        await reopened.EnsureAsync();

        Assert.Equal(BiblePhase.Ready, reopened.Status.Phase);
        Assert.Equal(36, reopened.VerseCount("JHN", 3));
    }

    [Fact]
    public async Task Updates_are_checked_at_most_once_a_day_with_if_none_match()
    {
        var stack = await SignedInAsync();
        var store = NewStore(stack);
        await store.EnsureAsync();
        stack.Fake.Calls.Clear();

        stack.Advance(TimeSpan.FromHours(3));
        await store.EnsureAsync();
        Assert.DoesNotContain(stack.Fake.Calls, c => c.Contains("download"));

        stack.Advance(TimeSpan.FromHours(30));
        await store.EnsureAsync();
        Assert.Single(stack.Fake.Calls, c => c == "GET /bible/translations/rvr1909/download");
        Assert.Equal(BiblePhase.Ready, store.Status.Phase);

        // Checked again just now, so no new request.
        stack.Fake.Calls.Clear();
        await store.EnsureAsync();
        Assert.Empty(stack.Fake.Calls);
    }

    [Fact]
    public async Task Offline_recheck_keeps_the_bible_working()
    {
        var stack = await SignedInAsync();
        var store = NewStore(stack);
        await store.EnsureAsync();
        stack.Offline = true;
        stack.Advance(TimeSpan.FromDays(2));

        await store.EnsureAsync();

        Assert.Equal(BiblePhase.Ready, store.Status.Phase);
        Assert.Equal(36, store.VerseCount("JHN", 3));
    }

    [Fact]
    public async Task Progress_is_reported_while_downloading()
    {
        var stack = await SignedInAsync();
        var store = NewStore(stack);
        var seen = new List<double>();
        store.StatusChanged += (_, _) =>
        {
            if (store.Status.Phase == BiblePhase.Downloading)
            {
                seen.Add(store.Status.Progress);
            }
        };

        await store.EnsureAsync();

        Assert.Contains(seen, p => p > 0);
        Assert.All(seen, p => Assert.InRange(p, 0, 1));
    }

    [Fact]
    public async Task Repository_exposes_the_store_to_the_picker()
    {
        var stack = await SignedInAsync();
        var repository = new LiveBibleRepository(NewStore(stack));

        var books = await repository.BooksAsync();

        Assert.Equal(66, books.Count);
        Assert.Equal(BiblePhase.Ready, repository.Status.Phase);
        Assert.Equal(36, await repository.VerseCountAsync("JHN", 3));
        Assert.Equal("Salmos", (await repository.BooksAsync()).Single(b => b.Id == "PSA").Name);
        Assert.Equal(6, (await repository.VersesAsync("PSA", 23)).Count);
    }
}
