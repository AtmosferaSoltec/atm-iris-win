using Iris.Core.Updates;

namespace Iris.Tests;

public class UpdateCheckTests
{
    private sealed class FakeApi(Func<CancellationToken, Task<AppVersionCheckDto>> get) : IAppVersionApi
    {
        public int Calls { get; private set; }

        public Task<AppVersionCheckDto> GetAsync(string currentVersion, CancellationToken ct)
        {
            Calls++;
            return get(ct);
        }
    }

    private sealed class FakeInstaller(bool installed, string? available) : IInstaller
    {
        public bool IsInstalled => installed;

        public int Searches { get; private set; }

        public Task<string?> FindUpdateAsync(CancellationToken ct)
        {
            Searches++;
            return Task.FromResult(available);
        }

        public Task DownloadAsync(IProgress<int> percent, CancellationToken ct) => Task.CompletedTask;

        public void ApplyAndRestart()
        {
        }
    }

    private static FakeApi Answer(bool required) =>
        new(_ => Task.FromResult(new AppVersionCheckDto("windows", "1.0.1", required, null, "Mejoras")));

    [Fact]
    public async Task Demands_the_update_when_the_api_requires_it_and_the_feed_has_it()
    {
        var update = await UpdateCheck.RunAsync(Answer(true), new FakeInstaller(true, "1.0.1"), "1.0.0");

        Assert.Equal(new RequiredUpdate("1.0.1", "Mejoras"), update);
    }

    [Fact]
    public async Task Does_nothing_when_the_version_is_current()
    {
        var installer = new FakeInstaller(true, "1.0.1");

        Assert.Null(await UpdateCheck.RunAsync(Answer(false), installer, "1.0.1"));
        Assert.Equal(0, installer.Searches);
    }

    [Fact]
    public async Task Does_not_block_when_the_feed_does_not_have_the_version_yet()
    {
        Assert.Null(await UpdateCheck.RunAsync(Answer(true), new FakeInstaller(true, null), "1.0.0"));
    }

    [Fact]
    public async Task Development_copy_does_not_ask_the_api()
    {
        var api = Answer(true);

        Assert.Null(await UpdateCheck.RunAsync(api, new FakeInstaller(false, "9.9.9"), "1.0.0"));
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task Api_error_lets_the_person_in()
    {
        var api = new FakeApi(_ => throw new HttpRequestException("sin conexión"));

        Assert.Null(await UpdateCheck.RunAsync(api, new FakeInstaller(true, "1.0.1"), "1.0.0"));
    }

    [Fact]
    public async Task Slow_api_lets_the_person_in_after_the_timeout()
    {
        var api = new FakeApi(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new AppVersionCheckDto("windows", "1.0.1", true, null, null);
        });

        var started = DateTime.UtcNow;
        var update = await UpdateCheck.RunAsync(api, new FakeInstaller(true, "1.0.1"), "1.0.0", TimeSpan.FromMilliseconds(100));

        Assert.Null(update);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("1.0.9", "1.0.10", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.1.0", "1.0.1", 0)]
    public void Compare_is_numeric_and_pads_missing_parts(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(AppVersion.Compare(a, b)));
}
