using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Iris.Core.Models;

namespace Iris.Core.Services.Mocks;

public sealed class MockAuthService : IAuthService
{
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(600);

    public async Task<UserSession> SignInAsync(SignInCredentials credentials)
    {
        await Task.Delay(Latency);
        var email = credentials.Email.Trim();
        if (email.StartsWith("error@", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException(AuthError.InvalidCredentials);
        }

        // Mockup phase: sign-in is free; an empty email falls back to the demo account.
        return SampleData.Session with { Email = email.Length == 0 ? SampleData.Session.Email : email };
    }

    public async Task<UserSession> SignUpAsync(SignUpRequest request)
    {
        await Task.Delay(Latency);
        if (request.Email.Trim().StartsWith("existe@", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthException(AuthError.EmailAlreadyInUse);
        }

        return new UserSession(Guid.NewGuid(), request.ChurchName.Trim(), request.LeaderName.Trim(), request.Email.Trim());
    }

    public Task RequestPasswordResetAsync(string email) => Task.Delay(Latency);
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

    public Task<IReadOnlyList<BibleBook>> BooksAsync() => Task.FromResult(SampleData.BibleBooks);

    public async Task<int> VerseCountAsync(string bookId, int chapter)
    {
        await Task.Delay(150);
        return VerseCount(bookId, chapter);
    }

    public async Task<IReadOnlyList<BibleVerse>> VersesAsync(string bookId, int chapter)
    {
        await Task.Delay(150);
        var book = SampleData.BibleBooks.First(b => b.Id == bookId);
        SampleData.KnownChapters.TryGetValue((bookId, chapter), out var known);
        return Enumerable.Range(1, VerseCount(bookId, chapter))
            .Select(n => new BibleVerse(n, known.Text is not null && known.Text.TryGetValue(n, out var text) ? text : $"Texto de ejemplo de {book.Name} {chapter}:{n}."))
            .ToList();
    }

    private static int VerseCount(string bookId, int chapter)
    {
        if (SampleData.KnownChapters.TryGetValue((bookId, chapter), out var known))
        {
            return known.Count;
        }

        // Deterministic 18–37 so the same chapter always has the same count.
        var hash = 17;
        foreach (var c in bookId)
        {
            hash = (hash * 31) + c;
        }

        hash = (hash * 31) + chapter;
        return 18 + (Math.Abs(hash) % 20);
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

/// <summary>No-op: the console simulates elapsed time. The live version will wrap Windows.Media.Playback.MediaPlayer.</summary>
public sealed class MockMediaPlaybackService : IMediaPlaybackService
{
    public void Play(Guid itemId) { }

    public void Pause() { }

    public void Resume() { }

    public void Stop() { }

    public void Seek(double seconds) { }

    public void SetLooping(bool isLooping) { }
}

/// <summary>Pretends a TV is connected; frames go nowhere. Useful on single-monitor machines.</summary>
public sealed class MockDisplayOutputService : IDisplayOutputService
{
    public ExternalDisplay? ConnectedDisplay() => new("Sala principal", "1920 × 1080");

    public void Present(ProjectionFrame frame) { }
}
