using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iris.Core.Models;
using Iris.Core.Services;

namespace Iris.Features.Bible;

public enum BibleStep
{
    Book,
    Chapter,
    Verse,
}

/// <summary>A tappable cell: a book (name + "{n} cap.") or a chapter/verse number.</summary>
public sealed record PickerCell(string Title, string? Detail, object Value, ICommand Command)
{
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>Book → chapter → verse picker (IRIS_SPEC §6.3).</summary>
public sealed partial class BiblePickerViewModel : ObservableObject, IDisposable
{
    private readonly IBibleRepository _bible;
    private readonly IUiDispatcher? _ui;
    private bool _disposed;
    private readonly Func<BibleBook, int, int, Task> _onVerseSelected;
    private IReadOnlyList<BibleBook> _books = [];

    public BiblePickerViewModel(IBibleRepository bible, Func<BibleBook, int, int, Task> onVerseSelected, IUiDispatcher? ui = null)
    {
        _bible = bible;
        _ui = ui;
        _onVerseSelected = onVerseSelected;
    }

    public string TranslationName => _bible.TranslationName;

    public IList<string> Testaments { get; } = ["Antiguo Testamento", "Nuevo Testamento"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookStep), nameof(IsChapterStep), nameof(IsVerseStep), nameof(IsNumberStep), nameof(CanGoBack), nameof(Title), nameof(Subtitle))]
    public partial BibleStep Step { get; set; }

    public bool IsBookStep => Step == BibleStep.Book;

    public bool IsChapterStep => Step == BibleStep.Chapter;

    public bool IsVerseStep => Step == BibleStep.Verse;

    public bool IsNumberStep => !IsBookStep;

    public bool CanGoBack => !IsBookStep;

    public string Title => Step switch
    {
        BibleStep.Book => "Biblia",
        BibleStep.Chapter => SelectedBook?.Name ?? "Biblia",
        _ => $"{SelectedBook?.Name} {SelectedChapter}",
    };

    public string Subtitle => (Step switch
    {
        BibleStep.Book => "Elige un libro",
        BibleStep.Chapter => "Elige un capítulo",
        _ => "Elige un versículo",
    }) + " · " + TranslationName;

    /// <summary>0 = Antiguo, 1 = Nuevo (default).</summary>
    [ObservableProperty]
    public partial int TestamentIndex { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSearching))]
    public partial string Query { get; set; } = string.Empty;

    public bool IsNotSearching => string.IsNullOrWhiteSpace(Query);

    public ObservableCollection<PickerCell> Books { get; } = [];

    public ObservableCollection<PickerCell> Numbers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoBooks))]
    public partial bool IsLoaded { get; set; }

    public bool HasNoBooks => IsLoaded && Books.Count == 0 && IsBibleReady;

    [ObservableProperty]
    public partial bool IsLoadingNumbers { get; set; }

    [ObservableProperty]
    public partial BibleBook? SelectedBook { get; set; }

    [ObservableProperty]
    public partial int SelectedChapter { get; set; }

    // ----- The Bible may still be downloading (first run) -----

    public bool IsBibleReady => _bible.Status.Phase == BiblePhase.Ready;

    public bool ShowsStatus => !IsBibleReady;

    public bool IsDownloadingBible => _bible.Status.Phase == BiblePhase.Downloading;

    public bool CanRetry => _bible.Status.Phase is BiblePhase.Failed or BiblePhase.NotDownloaded;

    public double ProgressPercent => _bible.Status.Progress * 100;

    public string StatusText => _bible.Status.Phase switch
    {
        BiblePhase.Downloading => $"Descargando la Biblia… {(int)Math.Round(_bible.Status.Progress * 100)} %",
        BiblePhase.Failed => _bible.Status.Message ?? "No pudimos descargar la Biblia.",
        _ => "Necesitas conexión para descargar la Biblia la primera vez.",
    };

    public async Task LoadAsync()
    {
        _bible.StatusChanged += OnBibleStatusChanged;
        _books = await _bible.BooksAsync();
        IsLoaded = true;
        FilterBooks();
        RefreshStatus();
    }

    public void Dispose()
    {
        _disposed = true;
        _bible.StatusChanged -= OnBibleStatusChanged;
    }

    [RelayCommand]
    private Task RetryAsync() => _bible.RetryAsync();

    private void OnBibleStatusChanged(object? sender, EventArgs e)
    {
        if (_ui is null)
        {
            return;
        }

        _ui.Post(async () =>
        {
            if (_disposed)
            {
                return;
            }

            RefreshStatus();
            if (IsBibleReady && _books.Count == 0)
            {
                _books = await _bible.BooksAsync();
                FilterBooks();
            }
        });
    }

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(IsBibleReady));
        OnPropertyChanged(nameof(ShowsStatus));
        OnPropertyChanged(nameof(IsDownloadingBible));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasNoBooks));
    }

    partial void OnTestamentIndexChanged(int value) => FilterBooks();

    partial void OnQueryChanged(string value) => FilterBooks();

    [RelayCommand]
    private void SelectBook(BibleBook book)
    {
        SelectedBook = book;
        Numbers.Clear();
        foreach (var chapter in Enumerable.Range(1, book.ChapterCount))
        {
            Numbers.Add(new PickerCell(chapter.ToString(CultureInfo.InvariantCulture), null, chapter, SelectChapterCommand));
        }

        Step = BibleStep.Chapter;
    }

    [RelayCommand]
    private async Task SelectChapterAsync(int chapter)
    {
        if (SelectedBook is null)
        {
            return;
        }

        SelectedChapter = chapter;
        Numbers.Clear();
        Step = BibleStep.Verse;
        IsLoadingNumbers = true;
        var count = await _bible.VerseCountAsync(SelectedBook.Id, chapter);
        IsLoadingNumbers = false;
        foreach (var verse in Enumerable.Range(1, count))
        {
            Numbers.Add(new PickerCell(verse.ToString(CultureInfo.InvariantCulture), null, verse, SelectVerseCommand));
        }
    }

    [RelayCommand]
    private async Task SelectVerseAsync(int verse)
    {
        if (SelectedBook is not null)
        {
            await _onVerseSelected(SelectedBook, SelectedChapter, verse);
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step == BibleStep.Verse && SelectedBook is not null)
        {
            SelectBook(SelectedBook);
        }
        else
        {
            Step = BibleStep.Book;
        }
    }

    private void FilterBooks()
    {
        var query = Normalize(Query);
        var matches = query.Length > 0
            ? _books.Where(b => Normalize(b.Name).Contains(query, StringComparison.Ordinal))
            : _books.Where(b => b.Testament == (TestamentIndex == 0 ? Testament.Old : Testament.New));

        Books.Clear();
        foreach (var book in matches)
        {
            Books.Add(new PickerCell(book.Name, $"{book.ChapterCount} cap.", book, SelectBookCommand));
        }

        OnPropertyChanged(nameof(HasNoBooks));
    }

    /// <summary>Accent- and case-insensitive search key.</summary>
    internal static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
