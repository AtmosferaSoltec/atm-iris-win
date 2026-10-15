using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Iris.Core.Updates;

/// <summary>What <c>GET /app-version</c> answers (api-contract §17).</summary>
public sealed record AppVersionCheckDto(string Platform, string LatestVersion, bool UpdateRequired, string? DownloadUrl, string? Notes);

/// <summary>The version the person must install before continuing.</summary>
public sealed record RequiredUpdate(string Version, string? Notes);

/// <summary>Asks the API whether this version is still valid. Public endpoint: no session needed.</summary>
public interface IAppVersionApi
{
    Task<AppVersionCheckDto> GetAsync(string currentVersion, CancellationToken ct);
}

/// <summary>
/// The installer side (Velopack in the real app): finds, downloads and applies the new package.
/// Kept free of WinUI so the rules around it can be tested. See docs/plans/12-distribucion-y-actualizaciones.
/// </summary>
public interface IInstaller
{
    /// <summary>False in a development copy: it was not put there by the installer, so there is nothing to update.</summary>
    bool IsInstalled { get; }

    /// <summary>The version in the feed when it is newer than this one, otherwise <c>null</c>. Throws when the feed is unreachable.</summary>
    Task<string?> FindUpdateAsync(CancellationToken ct);

    /// <summary>Downloads the update; <paramref name="percent"/> reports 0–100.</summary>
    Task DownloadAsync(IProgress<int> percent, CancellationToken ct);

    /// <summary>Closes the app, applies the downloaded update and starts the new version.</summary>
    void ApplyAndRestart();
}

/// <summary>Mock mode, and tests: nothing to install, nothing to ask.</summary>
public sealed class NoInstaller : IInstaller
{
    public bool IsInstalled => false;

    public Task<string?> FindUpdateAsync(CancellationToken ct) => Task.FromResult<string?>(null);

    public Task DownloadAsync(IProgress<int> percent, CancellationToken ct) => Task.CompletedTask;

    public void ApplyAndRestart()
    {
    }
}

/// <summary>Mock mode: never asks.</summary>
public sealed class NoAppVersionApi : IAppVersionApi
{
    public Task<AppVersionCheckDto> GetAsync(string currentVersion, CancellationToken ct) =>
        Task.FromResult(new AppVersionCheckDto("windows", currentVersion, false, null, null));
}

public static class AppVersion
{
    /// <summary>Dotted numeric versions ("1.0", "1.0.1"); missing parts are 0. Same rule as the API.</summary>
    public static int Compare(string a, string b)
    {
        var left = Parts(a);
        var right = Parts(b);
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var difference = (i < left.Length ? left[i] : 0) - (i < right.Length ? right[i] : 0);
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    /// <summary>The running app's version as "1.0.1" (the assembly's four parts, trailing zero dropped).</summary>
    public static string Current()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static int[] Parts(string version) =>
        version.Split('.').Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();
}

public static class UpdateCheck
{
    /// <summary>How long the app waits for the answer before letting the person in.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The update the person must install, or <c>null</c> to go on as usual. Never throws and never waits longer than
    /// <paramref name="timeout"/>: without a connection, with the API or the feed down, or when the API asks for a
    /// version the feed does not have yet, the app opens normally. An update is only demanded when it can be installed.
    /// </summary>
    public static async Task<RequiredUpdate?> RunAsync(IAppVersionApi api, IInstaller installer, string currentVersion, TimeSpan? timeout = null)
    {
        if (!installer.IsInstalled)
        {
            return null;
        }

        using var limit = new CancellationTokenSource(timeout ?? DefaultTimeout);
        try
        {
            var check = await api.GetAsync(currentVersion, limit.Token).WaitAsync(limit.Token);
            if (!check.UpdateRequired)
            {
                return null;
            }

            var available = await installer.FindUpdateAsync(limit.Token).WaitAsync(limit.Token);
            return available is null ? null : new RequiredUpdate(check.LatestVersion, check.Notes);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
