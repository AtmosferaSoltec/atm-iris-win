using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Iris.Core.Models;
using Iris.Core.Services;

namespace Iris.Features.LiveConsole;

/// <summary>A row in the SERVICIO list.</summary>
public sealed partial class ServiceItemViewModel(ServiceItem model, LiveConsoleViewModel owner) : ObservableObject
{
    public ServiceItem Model { get; } = model;

    public LiveConsoleViewModel Owner { get; } = owner;

    public Guid Id => Model.Id;

    public ServiceItemKind Kind => Model.Kind;

    public string Title => Model.Title;

    public string Subtitle => Model.Subtitle;

    [ObservableProperty]
    public partial string Number { get; set; } = "01";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }

    /// <summary>Used by the list container as its accessible name.</summary>
    public override string ToString() => $"{Title}, {KindStyleName}";

    private string KindStyleName => Kind switch
    {
        ServiceItemKind.Song => "Letra",
        ServiceItemKind.Scripture => "Pasaje",
        ServiceItemKind.Announcement => "Anuncio",
        ServiceItemKind.Music => "Música",
        ServiceItemKind.Image => "Imagen",
        _ => "Video",
    };
}

/// <summary>A slide thumbnail in the workspace grid. Cards never show the background.</summary>
public sealed partial class SlideCardViewModel(int index, Slide slide, LiveConsoleViewModel owner) : ObservableObject
{
    public int Index { get; } = index;

    public LiveConsoleViewModel Owner { get; } = owner;

    public string? Label { get; } = slide.Label;

    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    public ProjectionFrame Frame { get; } = new(null, slide.Content);

    public string AccessibleName => slide.Content is TextContent text ? $"{Label} {text.Body}".Trim() : Label ?? string.Empty;

    [ObservableProperty]
    public partial bool IsLive { get; set; }
}

public sealed partial class BackgroundOptionViewModel(ProjectionBackground model, LiveConsoleViewModel owner) : ObservableObject
{
    public ProjectionBackground Model { get; } = model;

    public LiveConsoleViewModel Owner { get; } = owner;

    public string Name => Model.Name;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Music or video playing (simulated clock in the mockup).</summary>
public sealed partial class PlaybackViewModel : ObservableObject
{
    private readonly IMediaPlaybackService _player;
    private bool _isTicking;

    public PlaybackViewModel(ServiceItem item, double duration, IMediaPlaybackService player)
    {
        _player = player;
        ItemId = item.Id;
        Kind = item.Kind;
        Title = item.Title;
        Duration = Math.Max(1, duration);
    }

    public Guid ItemId { get; }

    public ServiceItemKind Kind { get; }

    public string Title { get; }

    public double Duration { get; }

    public bool IsVideo => Kind == ServiceItemKind.Video;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText), nameof(RemainingText))]
    public partial double Elapsed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsPaused), nameof(PlayPauseName))]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsLooping { get; set; }

    public bool IsPaused => !IsPlaying;

    public double Progress => Elapsed / Duration;

    public string ElapsedText => DurationText.Format(Elapsed);

    public string RemainingText => "-" + DurationText.Format(Duration - Math.Floor(Elapsed));

    public string StatusText => !IsPlaying ? "En pausa" : IsVideo ? "Reproduciendo en el TV" : "Sonando en el salón";

    public string PlayPauseName => IsPlaying ? "Pausar" : "Reproducir";

    /// <summary>Advances the simulated clock without treating it as a user seek.</summary>
    public void Tick(double seconds)
    {
        _isTicking = true;
        Elapsed = Math.Min(Duration, Elapsed + seconds);
        _isTicking = false;
    }

    public void Rewind()
    {
        _isTicking = true;
        Elapsed = 0;
        _isTicking = false;
    }

    partial void OnElapsedChanged(double value)
    {
        if (!_isTicking)
        {
            _player.Seek(value);
        }
    }
}
