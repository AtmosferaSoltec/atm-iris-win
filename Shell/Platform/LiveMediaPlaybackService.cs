using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Iris.Core.Services;
using Microsoft.Extensions.Logging;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Iris.Shell.Platform;

/// <summary>
/// Real audio and video (IRIS_SPEC §7.4) over <see cref="MediaPlayer"/>. One player (<see cref="Main"/>) plays music in the
/// room and video on the TV window; for the small EN VIVO preview of a video a second, muted player
/// (<see cref="Preview"/>) follows the first one's position — a player renders into one element at a time, so two players
/// are the stable way to show the same video twice. Only one playback exists at a time. The system media controls
/// (volume flyout, keyboard keys) show the title and can play and pause.
/// </summary>
public sealed class LiveMediaPlaybackService : IMediaPlaybackService, IDisposable
{
    private const double MaxDriftSeconds = 0.4;

    private readonly IUiDispatcher _ui;
    private readonly ILogger<LiveMediaPlaybackService> _log;
    private readonly CancellationTokenSource _lifetime = new();
    private PlaybackRequest? _current;
    private bool _looping;

    public LiveMediaPlaybackService(IUiDispatcher ui, ILogger<LiveMediaPlaybackService> log)
    {
        _ui = ui;
        _log = log;
        Main.PlaybackSession.PositionChanged += (_, _) => Report();
        Main.PlaybackSession.PlaybackStateChanged += (_, _) => Report();
        Main.PlaybackSession.NaturalDurationChanged += (_, _) => Report();
        Main.MediaEnded += (_, _) => _ui.Post(() => Ended?.Invoke(this, EventArgs.Empty));
        Main.MediaFailed += (_, args) =>
        {
            _log.LogWarning("No se pudo reproducir: {Error} {Message}", args.Error, args.ErrorMessage);
            _ui.Post(() => Ended?.Invoke(this, EventArgs.Empty));
        };
        _ = KeepPreviewInSyncAsync(_lifetime.Token);
    }

    /// <summary>Plays the music and the video on the TV (the audio of a video comes from here too).</summary>
    public MediaPlayer Main { get; } = new() { AutoPlay = false, AudioCategory = MediaPlayerAudioCategory.Media };

    /// <summary>Muted mirror of a video for the console preview.</summary>
    public MediaPlayer Preview { get; } = new() { AutoPlay = false, IsMuted = true };

    public event EventHandler<PlaybackProgress>? ProgressChanged;

    public event EventHandler? Ended;

    public bool Play(PlaybackRequest request)
    {
        if (request.Path is not { } path || !File.Exists(path))
        {
            return false;
        }

        Stop();
        _current = request;
        try
        {
            Main.IsLoopingEnabled = _looping = false;
            Main.Source = Describe(MediaSource.CreateFromUri(new Uri(path)), request);
            Main.Play();
            if (request.IsVideo)
            {
                Preview.IsLoopingEnabled = false;
                Preview.Source = MediaSource.CreateFromUri(new Uri(path));
                Preview.Play();
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning("No se pudo abrir el archivo de reproducción: {Error}", ex.Message);
            Stop();
            return false;
        }
    }

    public void Pause()
    {
        Main.Pause();
        Preview.Pause();
    }

    public void Resume()
    {
        if (_current is null)
        {
            return;
        }

        Main.Play();
        if (_current.IsVideo)
        {
            Preview.Play();
        }
    }

    public void Stop()
    {
        _current = null;
        Main.Pause();
        Preview.Pause();
        Main.Source = null;
        Preview.Source = null;
    }

    public void Seek(double seconds)
    {
        if (_current is null)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
        Main.PlaybackSession.Position = position;
        if (_current.IsVideo)
        {
            Preview.PlaybackSession.Position = position;
        }
    }

    public void SetLooping(bool isLooping)
    {
        _looping = isLooping;
        Main.IsLoopingEnabled = isLooping;
        Preview.IsLoopingEnabled = isLooping;
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        Main.Dispose();
        Preview.Dispose();
    }

    // Title and play/pause in the Windows media overlay.
    private static MediaPlaybackItem Describe(MediaSource source, PlaybackRequest request)
    {
        var item = new MediaPlaybackItem(source);
        var props = item.GetDisplayProperties();
        if (request.IsVideo)
        {
            props.Type = MediaPlaybackType.Video;
            props.VideoProperties.Title = request.Title;
        }
        else
        {
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = request.Title;
            props.MusicProperties.Artist = "Iris";
        }

        item.ApplyDisplayProperties(props);
        return item;
    }

    private void Report()
    {
        var session = Main.PlaybackSession;
        var duration = session.NaturalDuration.TotalSeconds;
        var progress = new PlaybackProgress(
            session.Position.TotalSeconds,
            duration > 0 ? duration : _current?.Duration.TotalSeconds ?? 0,
            session.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening);
        if (_current is not null)
        {
            _ui.Post(() => ProgressChanged?.Invoke(this, progress));
        }
    }

    // The preview must not drift from the TV: correct it once a second when it is off by more than a few tenths.
    private async Task KeepPreviewInSyncAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                if (_current is { IsVideo: true } && Preview.Source is not null)
                {
                    var difference = Math.Abs((Main.PlaybackSession.Position - Preview.PlaybackSession.Position).TotalSeconds);
                    if (difference > MaxDriftSeconds)
                    {
                        Preview.PlaybackSession.Position = Main.PlaybackSession.Position;
                    }

                    var playing = Main.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
                    if (playing && Preview.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
                    {
                        Preview.Play();
                    }
                    else if (!playing && Preview.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                    {
                        Preview.Pause();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
