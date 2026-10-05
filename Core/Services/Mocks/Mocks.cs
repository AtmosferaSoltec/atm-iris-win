using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Iris.Core.Models;

namespace Iris.Core.Services.Mocks;

public sealed class MockAuthService : IAuthService
{
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(600);

    private UserSession? _current;

    public async Task<UserSession> SignInAsync(SignInCredentials credentials)
    {
        await Task.Delay(Latency);
        var email = credentials.Email.Trim();
        if (email.StartsWith("error@", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException(AuthError.InvalidCredentials);
        }

        // Design mode: sign-in is free; an empty email falls back to the demo account.
        return _current = SampleData.Session with { Email = email.Length == 0 ? SampleData.Session.Email : email };
    }

    public async Task<UserSession> SignUpAsync(SignUpRequest request)
    {
        await Task.Delay(Latency);
        if (request.Email.Trim().StartsWith("existe@", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException(AuthError.EmailAlreadyInUse);
        }

        var church = new ChurchIdentity(Guid.NewGuid(), request.ChurchName.Trim(), "America/Lima");
        return _current = SampleData.Session with
        {
            UserId = Guid.NewGuid(),
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            Church = church,
            Churches = [new ChurchSummary(church.Id, church.Name, Role.Owner)],
        };
    }

    public Task SignOutAsync()
    {
        _current = null;
        return Task.CompletedTask;
    }

    public Task SignOutAllAsync() => SignOutAsync();

    public Task RequestPasswordResetAsync(string email) => Task.Delay(Latency);

    public async Task VerifyResetCodeAsync(string email, string code)
    {
        await Task.Delay(Latency);
        if (code != "123456")
        {
            throw new AuthException(AuthError.Unknown, "El código es incorrecto o venció.", "RESET_CODE_INVALID", new Dictionary<string, string> { ["code"] = "El código es incorrecto o venció." });
        }
    }

    public Task ResetPasswordAsync(string email, string code, string password, string passwordConfirmation) => Task.Delay(Latency);

    public Task<UserSession> SwitchChurchAsync(Guid churchId) => Task.FromResult(_current ?? SampleData.Session);

    public Task<UserSession?> CurrentSessionAsync() => Task.FromResult(_current);

    public Task<UserSession?> RestoreAsync() => Task.FromResult<UserSession?>(null);

#pragma warning disable CS0067 // Never raised: the mock session does not expire.
    public event EventHandler? SessionExpired;

    public event EventHandler<UserSession>? SessionUpdated;
#pragma warning restore CS0067
}

public sealed class MockShowcaseContentProvider : IShowcaseContentProvider
{
    public IReadOnlyList<ShowcaseItem> Items() => SampleData.Showcase;
}

public sealed class MockServicePlanRepository : IServicePlanRepository
{
    public async Task<ServicePlan> CurrentServiceAsync()
    {
        await Task.Delay(450);
        return SampleData.SundayService();
    }
}

public sealed class MockBackgroundRepository : IBackgroundRepository
{
    public Task<IReadOnlyList<ProjectionBackground>> BackgroundsAsync() => Task.FromResult(SampleData.Backgrounds);
}

public sealed class MockBibleRepository : IBibleRepository
{
    public string TranslationName => "Reina-Valera 1909";

    public BibleStatus Status => BibleStatus.Ready;

#pragma warning disable CS0067 // Never raised: the design-mode Bible is always there.
    public event EventHandler? StatusChanged;
#pragma warning restore CS0067

    public Task RetryAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<BibleBook>> BooksAsync() => Task.FromResult(SampleData.BibleBooks);

    public async Task<int> VerseCountAsync(string bookId, int chapter)
    {
        await Task.Delay(150);
        return SampleBible.VerseCount(bookId, chapter);
    }

    public async Task<IReadOnlyList<BibleVerse>> VersesAsync(string bookId, int chapter)
    {
        await Task.Delay(150);
        return SampleBible.Verses(bookId, chapter);
    }
}

public sealed class MockLibraryRepository : ILibraryRepository
{
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(300);

    public async Task<IReadOnlyList<LyricSheet>> LyricsAsync()
    {
        await Task.Delay(Latency);
        return SampleData.Lyrics;
    }

    public async Task<IReadOnlyList<MediaAsset>> MediaAsync(MediaKind kind)
    {
        await Task.Delay(Latency);
        return kind switch
        {
            MediaKind.Music => SampleData.Music,
            MediaKind.Image => SampleData.Images,
            _ => SampleData.Videos,
        };
    }
}

/// <summary>No real player: the console simulates elapsed time. <see cref="LiveMediaPlaybackService"/> is the real one.</summary>
public sealed class MockMediaPlaybackService : IMediaPlaybackService
{
#pragma warning disable CS0067 // Never raised: nothing really plays.
    public event EventHandler<PlaybackProgress>? ProgressChanged;

    public event EventHandler? Ended;
#pragma warning restore CS0067

    public bool Play(PlaybackRequest request) => false;

    public void Pause() { }

    public void Resume() { }

    public void Stop() { }

    public void Seek(double seconds) { }

    public void SetLooping(bool isLooping) { }
}

/// <summary>Pretends a TV is connected; frames go nowhere. Useful on single-monitor machines.</summary>
public sealed class MockDisplayOutputService : IDisplayOutputService
{
    private static readonly ExternalDisplay Hall = new("Sala principal", "1920 × 1080", "mock");

#pragma warning disable CS0067 // Never raised: the pretend TV never changes.
    public event EventHandler? DisplayChanged;
#pragma warning restore CS0067

    public ExternalDisplay? ConnectedDisplay() => Hall;

    public IReadOnlyList<ExternalDisplay> Displays() => [Hall];

    public void Choose(ExternalDisplay display) { }

    public void Present(ProjectionFrame frame) { }
}
