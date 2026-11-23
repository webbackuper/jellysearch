using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>The fields a word can be found in.</summary>
[Flags]
public enum TextField : ushort
{
    None = 0,
    Name = 1,
    Original = 2,
    Series = 4,
    Actor = 8,
    Director = 16,
    Writer = 32,
    OtherPerson = 64,
    Genre = 128,
    Tag = 256,
    Studio = 512,
    Overview = 1024,

    /// <summary>The title fields.</summary>
    Titles = Name | Original | Series,

    /// <summary>The people fields.</summary>
    People = Actor | Director | Writer | OtherPerson,

    /// <summary>Every field.</summary>
    All = Titles | People | Genre | Tag | Studio | Overview
}

/// <summary>A word found in a field of a document.</summary>
public readonly record struct Posting(int Doc, TextField Field);

/// <summary>A person of a document, folded.</summary>
public sealed record IndexedPerson(string Folded, string[] Tokens, PersonRole Role);

/// <summary>
/// An immutable in-memory index of the library: an inverted index of the folded words (for the free
/// text), the vocabulary (for prefixes and typos) and the folded values of each document (for filters).
/// Rebuilt in the background when the library changes; queries use the snapshot they started with.
/// </summary>
public sealed class SearchIndex
{
    private readonly Dictionary<string, List<Posting>> _postings = new(StringComparer.Ordinal);
    private readonly string[] _vocabulary;
    private readonly Dictionary<int, List<string>> _fuzzyByLength = new();

    private SearchIndex(IReadOnlyList<SearchDocument> docs)
    {
        Docs = docs;
        var n = docs.Count;
        NameFolded = new string[n];
        OriginalFolded = new string[n];
        SeriesFolded = new string[n];
        OverviewFolded = new string[n];
        DocGenres = new string[n][];
        DocTags = new string[n][];
        DocStudios = new string[n][];
        DocPeople = new IndexedPerson[n][];
        DocMediaId = new string?[n];
        DocById = new Dictionary<Guid, int>(n);
        var fuzzyWords = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < n; i++)
        {
            var d = docs[i];
            DocById[d.Id] = i;
            NameFolded[i] = TextFold.Fold(d.Name);
            OriginalFolded[i] = TextFold.Fold(d.OriginalTitle);
            SeriesFolded[i] = TextFold.Fold(d.SeriesName);
            OverviewFolded[i] = TextFold.Fold(d.Overview);
            AddWords(i, NameFolded[i], TextField.Name, fuzzyWords, true);
            AddWords(i, OriginalFolded[i], TextField.Original, fuzzyWords, true);
            AddWords(i, SeriesFolded[i], TextField.Series, fuzzyWords, true);
            AddWords(i, OverviewFolded[i], TextField.Overview, null, false);

            DocGenres[i] = FoldAll(d.Genres, Genres);
            DocTags[i] = FoldAll(d.Tags, Tags);
            DocStudios[i] = FoldAll(d.Studios, Studios);
            foreach (var g in DocGenres[i])
            {
                AddWords(i, g, TextField.Genre, fuzzyWords, true);
            }

            foreach (var t in DocTags[i])
            {
                AddWords(i, t, TextField.Tag, fuzzyWords, true);
            }

            foreach (var s in DocStudios[i])
            {
                AddWords(i, s, TextField.Studio, fuzzyWords, true);
            }

            var people = new List<IndexedPerson>(d.People.Count);
            foreach (var p in d.People)
            {
                var folded = TextFold.Fold(p.Name);
                if (folded.Length == 0)
                {
                    continue;
                }

                people.Add(new IndexedPerson(folded, folded.Split(' '), p.Role));
                if (!PeopleNames.TryGetValue(folded, out var known))
                {
                    PeopleNames[folded] = (p.Name, RoleField(p.Role));
                }
                else
                {
                    PeopleNames[folded] = (known.Display, known.Roles | RoleField(p.Role));
                }

                AddWords(i, folded, RoleField(p.Role), fuzzyWords, true);
            }

            DocPeople[i] = people.ToArray();

            if (!string.IsNullOrEmpty(d.MediaId))
            {
                var media = TextFold.Fold(d.MediaId);
                DocMediaId[i] = media;
                MediaIds.TryAdd(media, d.MediaId);
            }
        }

        _vocabulary = _postings.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        foreach (var w in fuzzyWords)
        {
            if (!_fuzzyByLength.TryGetValue(w.Length, out var list))
            {
                _fuzzyByLength[w.Length] = list = new List<string>();
            }

            list.Add(w);
        }
    }

    /// <summary>An empty index (before the first build).</summary>
    public static SearchIndex Empty { get; } = new([]);

    public IReadOnlyList<SearchDocument> Docs { get; }

    public Dictionary<Guid, int> DocById { get; }

    internal string[] NameFolded { get; }

    internal string[] OriginalFolded { get; }

    internal string[] SeriesFolded { get; }

    internal string[] OverviewFolded { get; }

    internal string[][] DocGenres { get; }

    internal string[][] DocTags { get; }

    internal string[][] DocStudios { get; }

    internal IndexedPerson[][] DocPeople { get; }

    internal string?[] DocMediaId { get; }

    /// <summary>Folded genre → genre as written in the library.</summary>
    public Dictionary<string, string> Genres { get; } = new(StringComparer.Ordinal);

    /// <summary>Folded tag → tag.</summary>
    public Dictionary<string, string> Tags { get; } = new(StringComparer.Ordinal);

    /// <summary>Folded studio → studio.</summary>
    public Dictionary<string, string> Studios { get; } = new(StringComparer.Ordinal);

    /// <summary>Folded MediaID → MediaID.</summary>
    public Dictionary<string, string> MediaIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Folded person name → name and the roles seen.</summary>
    public Dictionary<string, (string Display, TextField Roles)> PeopleNames { get; } = new(StringComparer.Ordinal);

    /// <summary>Builds an index of <paramref name="docs"/>.</summary>
    public static SearchIndex Build(IEnumerable<SearchDocument> docs) => new(docs.ToList());

    /// <summary>The documents holding <paramref name="token"/> (folded).</summary>
    public IReadOnlyList<Posting> Postings(string token)
        => _postings.TryGetValue(token, out var list) ? list : [];

    /// <summary>
    /// The indexed words a query word stands for, with a quality: 1 for the word itself, 0.8 for a word
    /// it starts (when <paramref name="prefix"/>), 0.65 / 0.5 for a word one / two typos away.
    /// </summary>
    public List<(string Word, float Quality)> Expand(string term, bool prefix, bool fuzzy = true)
    {
        var result = new List<(string, float)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (_postings.ContainsKey(term))
        {
            result.Add((term, 1f));
            seen.Add(term);
        }

        if (prefix)
        {
            var start = Array.BinarySearch(_vocabulary, term, StringComparer.Ordinal);
            if (start < 0)
            {
                start = ~start;
            }

            for (var i = start; i < _vocabulary.Length && result.Count < 400; i++)
            {
                var w = _vocabulary[i];
                if (!w.StartsWith(term, StringComparison.Ordinal))
                {
                    break;
                }

                if (seen.Add(w))
                {
                    result.Add((w, 0.8f));
                }
            }
        }

        var allowed = fuzzy ? Fuzzy.Allowed(term.Length) : 0;
        if (allowed > 0 && !term.All(char.IsDigit))
        {
            for (var len = term.Length - allowed; len <= term.Length + allowed; len++)
            {
                if (!_fuzzyByLength.TryGetValue(len, out var words))
                {
                    continue;
                }

                foreach (var w in words)
                {
                    if (seen.Contains(w) || char.IsDigit(w[0]))
                    {
                        continue;
                    }

                    var d = Fuzzy.Distance(term, w, allowed);
                    if (d <= allowed)
                    {
                        seen.Add(w);
                        result.Add((w, d == 1 ? 0.65f : 0.5f));
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The folded values of <paramref name="vocabulary"/> that <paramref name="value"/> designates:
    /// the same value, its singular, a value it starts (4 letters or more) or a value one typo away.
    /// With <paramref name="exact"/>, only the same value.
    /// </summary>
    public static HashSet<string> MatchValues(IReadOnlyDictionary<string, string> vocabulary, string value, bool exact)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var folded = TextFold.Fold(value);
        if (folded.Length == 0)
        {
            return result;
        }

        var singular = string.Join(' ', folded.Split(' ').Select(TextFold.Singular));
        foreach (var key in vocabulary.Keys)
        {
            if (key == folded)
            {
                result.Add(key);
                continue;
            }

            if (exact)
            {
                continue;
            }

            var keySingular = string.Join(' ', key.Split(' ').Select(TextFold.Singular));
            if (keySingular == singular
                || (folded.Length >= 4 && (key.StartsWith(folded, StringComparison.Ordinal) || key.Contains(" " + folded, StringComparison.Ordinal)))
                || (folded.Length >= 5 && Fuzzy.Similar(singular, keySingular)))
            {
                result.Add(key);
            }
        }

        return result;
    }

    /// <summary>
    /// The folded names of the people that <paramref name="value"/> designates: every word of the value
    /// is a word of the name (or starts it, or is one typo away), in any order ("willis", "bruce wilis").
    /// </summary>
    public HashSet<string> MatchPeople(string value, TextField roles, bool strict = false)
    {
        var words = TextFold.Tokens(value);
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (words.Length == 0)
        {
            return result;
        }

        foreach (var (name, info) in PeopleNames)
        {
            if ((info.Roles & roles) == 0)
            {
                continue;
            }

            var nameWords = name.Split(' ');
            if (words.All(w => nameWords.Any(n => n == w || (!strict && ((w.Length >= 3 && n.StartsWith(w, StringComparison.Ordinal)) || Fuzzy.Similar(w, n))))))
            {
                result.Add(name);
            }
        }

        return result;
    }

    internal static TextField RoleField(PersonRole role) => role switch
    {
        PersonRole.Actor => TextField.Actor,
        PersonRole.Director => TextField.Director,
        PersonRole.Writer => TextField.Writer,
        _ => TextField.OtherPerson
    };

    private static string[] FoldAll(IReadOnlyList<string> values, Dictionary<string, string> vocabulary)
    {
        var result = new List<string>(values.Count);
        foreach (var v in values)
        {
            var folded = TextFold.Fold(v);
            if (folded.Length == 0)
            {
                continue;
            }

            result.Add(folded);
            vocabulary.TryAdd(folded, v);
        }

        return result.ToArray();
    }

    private void AddWords(int doc, string folded, TextField field, HashSet<string>? fuzzyWords, bool keepStopWords)
    {
        if (folded.Length == 0)
        {
            return;
        }

        foreach (var w in folded.Split(' ').Distinct(StringComparer.Ordinal))
        {
            if (!keepStopWords && (w.Length < 2 || TextFold.IsStopWord(w)))
            {
                continue;
            }

            if (!_postings.TryGetValue(w, out var list))
            {
                _postings[w] = list = new List<Posting>();
            }

            // one posting per document and field
            if (list.Count == 0 || list[^1].Doc != doc || (list[^1].Field & field) == 0)
            {
                list.Add(new Posting(doc, field));
            }

            if (fuzzyWords is not null && w.Length >= 4)
            {
                fuzzyWords.Add(w);
            }
        }
    }
}
