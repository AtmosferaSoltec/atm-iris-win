using Iris.Core.Media;
using Iris.Core.Models;
using Iris.Core.Networking;
using Iris.Core.Networking.Dto;
using Iris.Core.Networking.Fake;
using Iris.Core.Services.Live;
using Iris.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Iris.Tests;

public sealed class MediaCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iris-tests-" + Guid.NewGuid().ToString("N"));

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

    private sealed class FixedDisk(long free) : IDiskSpace
    {
        public long Free { get; set; } = free;

        public long FreeBytes(string folder) => Free;
    }

    private MediaCache NewCache(SyncTestStack s, IDiskSpace? disk = null) => new(
        s.Store,
        s.Stack.Api,
        () => new HttpClient(s.Stack.Fake, disposeHandler: false),
        s.Session,
        _root,
        disk,
        NullLogger<MediaCache>.Instance);

    private static async Task<SyncTestStack> ReadyAsync()
    {
        var s = new SyncTestStack();
        await s.SignInAsync();
        await s.Engine.SyncNowAsync(SyncReason.SignedIn);
        return s;
    }

    [Fact]
    public async Task Reconcile_downloads_every_file_and_marks_it_ready()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);

        await cache.ReconcileAsync();

        var media = await s.Store.GetMediaAsync();
        Assert.Equal(3, media.Count);
        foreach (var item in media)
        {
            var state = cache.StateOf(item);
            Assert.Equal(MediaAvailability.Ready, state.Availability);
            Assert.True(File.Exists(state.Path));
            Assert.Equal(item.SizeBytes, new FileInfo(state.Path!).Length);
        }
    }

    [Fact]
    public async Task A_second_reconcile_downloads_nothing_new()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);
        await cache.ReconcileAsync();
        s.Stack.Fake.Calls.Clear();

        await cache.ReconcileAsync();

        Assert.DoesNotContain(s.Stack.Fake.Calls, c => c.Contains("download-url"));
    }

    [Fact]
    public async Task Files_of_deleted_media_are_removed()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);
        await cache.ReconcileAsync();
        var gone = (await s.Store.GetMediaAsync()).First(m => m.Kind == "image");
        var path = cache.StateOf(gone).Path!;

        FakeRows.SoftDelete(s.Stack.Fake.Db, s.ChurchId, FakeKind.Media, gone.Id);
        await s.Engine.SyncNowAsync(SyncReason.Manual);
        await cache.ReconcileAsync();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_replaced_file_is_downloaded_again_under_the_new_name()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);
        await cache.ReconcileAsync();
        var item = (await s.Store.GetMediaAsync()).First(m => m.Kind == "image");
        var oldPath = cache.StateOf(item).Path!;

        var updated = item with { Title = "Otro título", UpdatedAt = item.UpdatedAt.AddMinutes(5) };
        FakeRows.Put(s.Stack.Fake.Db, s.ChurchId, FakeKind.Media, item.Id, updated, IrisJsonContext.Default.MediaAssetDto);
        await s.Engine.SyncNowAsync(SyncReason.Manual);
        await cache.ReconcileAsync();

        var fresh = (await s.Store.GetMediaAsync()).First(m => m.Id == item.Id);
        Assert.NotEqual(oldPath, cache.StateOf(fresh).Path);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(cache.StateOf(fresh).Path));
    }

    [Fact]
    public async Task An_interrupted_download_resumes_from_the_part_file()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);
        var audio = (await s.Store.GetMediaAsync()).First(m => m.Kind == "audio");
        var final = cache.PathFor(audio)!;
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);

        // The first 1000 bytes were saved before the app closed.
        var whole = FakeSamples.ToneWav(10, 440);
        await File.WriteAllBytesAsync(final + ".part", whole[..1000]);
        s.Stack.Fake.Calls.Clear();
        await cache.ReconcileAsync();

        Assert.Equal(whole, await File.ReadAllBytesAsync(final));
        Assert.False(File.Exists(final + ".part"));
    }

    [Fact]
    public async Task Videos_are_not_downloaded_with_less_than_one_gigabyte_free()
    {
        using var s = await ReadyAsync();
        var video = new MediaAssetDto(Guid.NewGuid(), "video", "Testimonio", null, "testimonio.mp4", "video/mp4", 4096, 30, 1920, 1080, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        FakeRows.Put(s.Stack.Fake.Db, s.ChurchId, FakeKind.Media, video.Id, video, IrisJsonContext.Default.MediaAssetDto);
        await s.Engine.SyncNowAsync(SyncReason.Manual);
        var disk = new FixedDisk(200L * 1024 * 1024);
        var cache = NewCache(s, disk);
        var warned = 0;
        cache.LowDiskSpaceChanged += (_, _) => warned++;

        await cache.ReconcileAsync();

        Assert.True(cache.LowDiskSpace);
        Assert.Equal(1, warned);
        var stored = (await s.Store.GetMediaAsync()).Single(m => m.Id == video.Id);
        Assert.Equal(MediaAvailability.NotDownloaded, cache.StateOf(stored).Availability);
        Assert.Equal(MediaAvailability.Ready, cache.StateOf((await s.Store.GetMediaAsync()).First(m => m.Kind == "image")).Availability);

        disk.Free = 50L * 1024 * 1024 * 1024;
        await cache.ReconcileAsync();
        Assert.False(cache.LowDiskSpace);
        Assert.Equal(MediaAvailability.Ready, cache.StateOf(stored).Availability);
    }

    [Fact]
    public async Task Library_exposes_file_state_and_background_images_only_once_downloaded()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);
        var library = new LiveLibraryRepository(s.Data, cache);
        var backgrounds = new LiveBackgroundRepository(s.Data, cache);

        var before = await library.MediaAsync(MediaKind.Image);
        Assert.All(before, m => Assert.False(m.IsAvailable));
        Assert.Equal(6, (await backgrounds.BackgroundsAsync()).Count);

        await cache.ReconcileAsync();

        var after = await library.MediaAsync(MediaKind.Image);
        Assert.All(after, m => Assert.True(m.IsAvailable && File.Exists(m.LocalPath)));
        var list = await backgrounds.BackgroundsAsync();
        Assert.Equal(7, list.Count);
        Assert.NotNull(list[^1].ImagePath);
        Assert.Equal(TimeSpan.FromSeconds(10), (await library.MediaAsync(MediaKind.Music)).Single().Duration);
    }

    [Fact]
    public async Task A_failed_download_is_reported_and_retried_next_time()
    {
        using var s = await ReadyAsync();
        var cache = NewCache(s);
        s.Stack.Offline = true;

        await cache.ReconcileAsync();
        var item = (await s.Store.GetMediaAsync()).First(m => m.Kind == "image");
        Assert.Equal(MediaAvailability.Failed, cache.StateOf(item).Availability);

        s.Stack.Offline = false;
        await cache.ReconcileAsync();
        Assert.Equal(MediaAvailability.Ready, cache.StateOf(item).Availability);
    }

    [Fact]
    public void Generated_png_has_valid_signature_and_dimensions()
    {
        var png = FakeSamples.GradientPng(64, 32, (0, 0, 0), (255, 255, 255));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(32, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }
}
