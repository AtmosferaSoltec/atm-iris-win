using System;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core;
using Iris.Core.Updates;
using Velopack;

namespace Iris.Shell.Platform;

/// <summary>
/// <see cref="IInstaller"/> over Velopack: the feed is the folder the release workflow publishes
/// (<see cref="WebLinks.WindowsFeed"/>). <see cref="IsInstalled"/> is false in a development copy.
/// </summary>
public sealed class VelopackInstaller : IInstaller
{
    private readonly UpdateManager _manager = new(WebLinks.WindowsFeed);
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<string?> FindUpdateAsync(CancellationToken ct)
    {
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAsync(IProgress<int> percent, CancellationToken ct)
    {
        if (_pending is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pending, percent.Report, ct);
    }

    public void ApplyAndRestart()
    {
        if (_pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
    }
}
