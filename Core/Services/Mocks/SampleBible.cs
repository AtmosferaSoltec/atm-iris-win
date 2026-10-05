using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;

namespace Iris.Core.Services.Mocks;

/// <summary>
/// Design-time Bible text: a few passages with real RVR1909 text (Salmos 23, Juan 1 and 3, Génesis 1) and generated
/// placeholder text everywhere else ("Texto de ejemplo de {Libro} {cap}:{v}."), with deterministic verse counts.
/// </summary>
public static class SampleBible
{
    public static int VerseCount(string bookId, int chapter)
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

    public static IReadOnlyList<BibleVerse> Verses(string bookId, int chapter)
    {
        var book = SampleData.BibleBooks.First(b => b.Id == bookId);
        SampleData.KnownChapters.TryGetValue((bookId, chapter), out var known);
        return Enumerable.Range(1, VerseCount(bookId, chapter))
            .Select(n => new BibleVerse(n, known.Text is not null && known.Text.TryGetValue(n, out var text) ? text : $"Texto de ejemplo de {book.Name} {chapter}:{n}."))
            .ToList();
    }

    /// <summary>USFM codes (api-contract §13) in canonical order, the same order as <see cref="SampleData.BibleBooks"/>.</summary>
    public static readonly string[] Usfm =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH",
        "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON",
        "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL",
        "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN",
        "JUD", "REV",
    ];
}
