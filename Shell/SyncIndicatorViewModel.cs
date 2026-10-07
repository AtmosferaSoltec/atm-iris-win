using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Formatting;
using Iris.Core.Media;
using Iris.Core.Services;
using Iris.Core.Sync;

namespace Iris.Shell;

/// <summary>
/// The sync indicator of the top bar and the "Preparando tu iglesia…" layer of Home. Wraps <see cref="ISyncService"/>,
/// moves its background events to the UI thread and refreshes the relative time ("hace 2 min") every 30 s.
/// </summary>
public sealed partial class SyncIndicatorViewModel : ObservableObject
{
    private readonly ISyncService _sync;
    private readonly IUiDispatcher _ui;
    private readonly IMediaCache _cache;
    private readonly Func<DateTimeOffset> _now;
    private CancellationTokenSource? _ticker;

    public SyncIndicatorViewModel(ISyncService sync, IUiDispatcher ui, IMediaCache cache, Func<DateTimeOffset>? now = null)
    {
        _ui = ui;
        _cache = cache;
        _cache.LowDiskSpaceChanged += (_, _) => _ui.Post(Refresh);
        _sync = sync;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _sync.StatusChanged += (_, _) => _ui.Post(Refresh);
        _sync.WriteDiscarded += (_, discarded) => _ui.Post(() =>
        {
            Notices = [.. Notices, $"No se pudo guardar «{discarded.Label}»: {discarded.Message}"];
            OnPropertyChanged(nameof(Notices));
            OnPropertyChanged(nameof(HasNotices));
        });
        Refresh();
    }

    /// <summary>Replaced (never mutated): the singleton outlives the top bars that bind to it.</summary>
    public IReadOnlyList<string> Notices { get; private set; } = [];

    public bool HasNotices => Notices.Count > 0;

    public SyncStatus Status => _sync.Status;

    public string Headline => SyncText.Headline(Status, _now());

    public string LastSyncText => Status.LastSync is { } last ? $"Última actualización: {SyncText.Ago(last, _now())}" : "Aún no se ha actualizado.";

    public string PendingText => Status.Pending > 0 ? $"{SyncText.Pending(Status.Pending)} por enviar." : "Todo está enviado.";

    public string? DiskWarning => _cache.LowDiskSpace ? "Queda poco espacio en disco: se pausó la descarga de videos." : null;

    public string? FailureText => Status.Phase == SyncPhase.Failed ? Status.Message : null;

    public bool IsSyncing => Status.Phase == SyncPhase.Syncing;

    public bool IsIdle => Status.Phase == SyncPhase.Idle;

    public bool IsOffline => Status.Phase == SyncPhase.Offline;

    public bool IsFailed => Status.Phase == SyncPhase.Failed;

    /// <summary>Hidden while a service runs (pulls are paused then).</summary>
    public bool IsVisible => !_sync.IsSuspended;

    public bool CanSyncNow => !IsSyncing && !_sync.IsSuspended;

    /// <summary>First download of the church in progress: Home shows its loading layer.</summary>
    public bool ShowsPreparing => Status.IsFirstSync && Status.Phase == SyncPhase.Syncing;

    /// <summary>The first download could not happen yet (no connection or an error): Home asks to retry.</summary>
    public bool NeedsConnection => Status.IsFirstSync && Status.Phase is SyncPhase.Offline or SyncPhase.Failed;

    public string PreparingDetail => Status.ItemsApplied > 0 ? $"{Status.ItemsApplied} elementos descargados" : "Esto solo pasa la primera vez.";

    public string NeedsConnectionText => Status.Phase == SyncPhase.Failed && Status.Message is { } message
        ? message
        : "Necesitas conexión para descargar tu iglesia la primera vez.";

    /// <summary>Starts the minute ticker (idempotent); call when the first window that shows the indicator loads.</summary>
    public void Activate()
    {
        if (_ticker is not null)
        {
            return;
        }

        _ticker = new CancellationTokenSource();
        _ = TickAsync(_ticker.Token);
    }

    [RelayCommand]
    private Task SyncNowAsync() => _sync.SyncNowAsync(SyncReason.Manual);

    [RelayCommand]
    private void DismissNotices()
    {
        Notices = [];
        OnPropertyChanged(nameof(Notices));
        OnPropertyChanged(nameof(HasNotices));
    }

    private void Refresh() => OnPropertyChanged(string.Empty);

    private async Task TickAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
