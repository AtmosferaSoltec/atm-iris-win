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
    Unknown,
}

public sealed class AuthException(AuthError error) : Exception(error.ToString())
{
    public AuthError Error { get; } = error;
}

public interface IAuthService
{
    Task<UserSession> SignInAsync(SignInCredentials credentials);

    Task<UserSession> SignUpAsync(SignUpRequest request);

    Task RequestPasswordResetAsync(string email);
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

public interface IBibleRepository
{
    string TranslationName { get; }

    Task<IReadOnlyList<BibleBook>> BooksAsync();

    Task<int> VerseCountAsync(string bookId, int chapter);

    Task<IReadOnlyList<BibleVerse>> VersesAsync(string bookId, int chapter);
}

public interface ILibraryRepository
{
    Task<IReadOnlyList<LyricSheet>> LyricsAsync();

    Task<IReadOnlyList<MediaAsset>> MediaAsync(MediaKind kind);
}

public interface IMediaPlaybackService
{
    void Play(Guid itemId);

    void Pause();

    void Resume();

    void Stop();

    void Seek(double seconds);

    void SetLooping(bool isLooping);
}

public interface IDisplayOutputService
{
    /// <summary>The TV / projector, or null when only the console monitor is connected.</summary>
    ExternalDisplay? ConnectedDisplay();

    /// <summary>Shows <paramref name="frame"/> on the TV.</summary>
    void Present(ProjectionFrame frame);
}

// ===== Church (IRIS_SPEC §6.1b, §10) =====

public interface IModuleSettingsRepository
{
    Task<ChurchModules> ModulesAsync();

    Task SaveAsync(ChurchModules modules);
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
}
