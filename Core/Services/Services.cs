using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Iris.Core.Models;

namespace Iris.Core.Services;

public enum AuthError
{
    InvalidCredentials,
    EmailAlreadyInUse,
    Network,
    SessionExpired,
    Unknown,
}

/// <summary>
/// Sign-in/up/recovery failure. For API errors <see cref="Code"/>, the Spanish message and the per-field errors
/// come from the server (api-contract §1.2) and are shown as is.
/// </summary>
public sealed class AuthException(AuthError error, string? message = null, string? code = null, IReadOnlyDictionary<string, string>? fieldErrors = null)
    : Exception(message ?? error.ToString())
{
    public AuthError Error { get; } = error;

    public string? Code { get; } = code;

    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();

    /// <summary>True when the server supplied a message worth showing (otherwise use the generic one for <see cref="Error"/>).</summary>
    public bool HasServerMessage => message is not null;
}

public interface IAuthService
{
    Task<UserSession> SignInAsync(SignInCredentials credentials);

    Task<UserSession> SignUpAsync(SignUpRequest request);

    /// <summary>Always clears the local session, even without a connection.</summary>
    Task SignOutAsync();

    Task SignOutAllAsync();

    Task RequestPasswordResetAsync(string email);

    Task VerifyResetCodeAsync(string email, string code);

    Task ResetPasswordAsync(string email, string code, string password, string passwordConfirmation);

    Task<UserSession> SwitchChurchAsync(Guid churchId);

    /// <summary>The stored session, read locally (no network); null when signed out or the refresh token expired. Used at startup.</summary>
    Task<UserSession?> RestoreAsync();

    /// <summary>The server rejected the refresh token: the session is over. Raised on any thread.</summary>
    event EventHandler? SessionExpired;

    /// <summary>Tokens refreshed, so the session (permissions, churches) may have changed. Raised on any thread.</summary>
    event EventHandler<UserSession>? SessionUpdated;

    /// <summary>The stored session, refreshed from <c>GET /auth/me</c> when there is a connection; null when signed out.</summary>
    Task<UserSession?> CurrentSessionAsync();
}

public interface IShowcaseContentProvider
{
    IReadOnlyList<ShowcaseItem> Items();
}

public interface IServicePlanRepository
{
    Task<ServicePlan> CurrentServiceAsync();
}

public interface IBackgroundRepository
{
    Task<IReadOnlyList<ProjectionBackground>> BackgroundsAsync();
}

public enum BiblePhase
{
    /// <summary>Nothing on this PC yet and no attempt running.</summary>
    NotDownloaded,
    Downloading,
    Ready,
    Failed,
}

/// <summary>State of the offline Bible; <see cref="Progress"/> is 0…1 while downloading.</summary>
public sealed record BibleStatus(BiblePhase Phase, double Progress = 0, string? Message = null)
{
    public static readonly BibleStatus Ready = new(BiblePhase.Ready);
}

public interface IBibleRepository
{
    string TranslationName { get; }

    BibleStatus Status { get; }

    /// <summary>Raised on any thread when <see cref="Status"/> changes.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Tries again to download the Bible (the picker's "Reintentar").</summary>
    Task RetryAsync();

    Task<IReadOnlyList<BibleBook>> BooksAsync();

    Task<int> VerseCountAsync(string bookId, int chapter);

    Task<IReadOnlyList<BibleVerse>> VersesAsync(string bookId, int chapter);
}

public interface ILibraryRepository
{
    Task<IReadOnlyList<LyricSheet>> LyricsAsync();

    Task<IReadOnlyList<MediaAsset>> MediaAsync(MediaKind kind);

    /// <summary>Starts downloading music or videos added to a service (api-contract §11); they stay on this PC
    /// afterwards. Images and backgrounds download on their own after each sync. Design data has nothing to fetch.</summary>
    Task DownloadAsync(IEnumerable<Guid> ids) => Task.CompletedTask;
}

/// <summary>What to play. <see cref="Path"/> is the cached file; without it (design data) nothing real can play.</summary>
public sealed record PlaybackRequest(Guid ItemId, bool IsVideo, string Title, string? Path, TimeSpan Duration);

/// <summary>Real time from the player. <see cref="Duration"/> is the real length of the file.</summary>
public sealed record PlaybackProgress(double Elapsed, double Duration, bool IsPlaying);

public interface IMediaPlaybackService
{
    /// <summary>
    /// Starts playing. Returns true when a real player took the file (progress and the end then arrive through events),
    /// false when there is no file to play, so the console simulates the clock instead (design data).
    /// </summary>
    bool Play(PlaybackRequest request);

    void Pause();

    void Resume();

    void Stop();

    void Seek(double seconds);

    void SetLooping(bool isLooping);

    /// <summary>Position, length and play/pause state, on the UI thread. Also raised when the system media controls pause or resume.</summary>
    event EventHandler<PlaybackProgress>? ProgressChanged;

    /// <summary>The media reached its end (never raised while looping), on the UI thread.</summary>
    event EventHandler? Ended;
}

public interface IDisplayOutputService
{
    /// <summary>The TV / projector in use, or null when only the console monitor is connected.</summary>
    ExternalDisplay? ConnectedDisplay();

    /// <summary>Every secondary monitor currently connected (the TV is one of them).</summary>
    IReadOnlyList<ExternalDisplay> Displays();

    /// <summary>Uses another secondary monitor as the TV ("Elegir pantalla").</summary>
    void Choose(ExternalDisplay display);

    /// <summary>The connected monitors or the chosen TV changed (a cable was plugged or pulled). Raised on the UI thread.</summary>
    event EventHandler? DisplayChanged;

    /// <summary>Shows <paramref name="frame"/> on the TV.</summary>
    void Present(ProjectionFrame frame);
}

// ===== Church (IRIS_SPEC §6.1b, §10) =====

public interface IModuleSettingsRepository
{
    Task<ChurchModules> ModulesAsync();

    Task SaveAsync(ChurchModules modules);

    /// <summary>Modules that exist in Iris today; one in <see langword="false"/> is not offered at
    /// all, not even in Configuración (api-contract §6). Everything by default.</summary>
    Task<ChurchModules> AvailableModulesAsync() => Task.FromResult(ChurchModules.All);
}

/// <summary>Typeface, size and default background of the projected lyrics (api-contract §6), same
/// for every console of the church.</summary>
public interface IProjectionSettingsRepository
{
    Task<ProjectionSettings> SettingsAsync();

    Task SaveAsync(ProjectionSettings settings);
}

public interface IServiceTypeRepository
{
    Task<IReadOnlyList<ServiceType>> ServiceTypesAsync();

    /// <summary>Inserts or replaces by id.</summary>
    Task SaveAsync(ServiceType type);

    /// <summary>Saved time records are kept.</summary>
    Task DeleteAsync(Guid id);
}

public interface IPeopleRepository
{
    Task<IReadOnlyList<Person>> PeopleAsync();

    Task<Person> AddAsync(string name);

    Task RenameAsync(Guid id, string name);

    /// <summary>Records keep the id and name; templates drop the person as suggested leader.</summary>
    Task DeleteAsync(Guid id);
}

public interface ITimeRecordRepository
{
    /// <summary>Most recent first.</summary>
    Task<IReadOnlyList<ServiceRecord>> RecordsAsync();

    /// <summary>Inserts or replaces by id.</summary>
    Task SaveAsync(ServiceRecord record);

    Task DeleteAsync(Guid id);

    /// <summary>True while the record (or a change to it) still waits to be sent to the server.</summary>
    Task<bool> IsPendingAsync(Guid id);
}
