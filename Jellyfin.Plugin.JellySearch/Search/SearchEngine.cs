using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>What a search needs to know about the user and the drives (filled lazily by the caller).</summary>
public sealed class SearchContext
{
    /// <summary>The items the user has watched (asked only when the query filters on it).</summary>
    public Func<HashSet<Guid>> Played { get; init; } = () => [];

    /// <summary>The user's favourites (asked only when the query filters on it).</summary>
    public Func<HashSet<Guid>> Favorites { get; init; } = () => [];

    /// <summary>Whether the drive of a MediaID is plugged in.</summary>
    public Func<string, bool> IsMediaPresent { get; init; } = _ => true;

    /// <summary>The IMDb rating and votes of an IMDb id.</summary>
    public Func<string, (double Rating, int Votes)?> Imdb { get; init; } = _ => null;

    public DateTime Now { get; init; } = DateTime.UtcNow;

    /// <summary>Seed of "tri:hasard" (the same order for every page of one search).</summary>
    public int RandomSeed { get; init; }

    /// <summary>Whether typos are forgiven.</summary>
    public bool Typos { get; init; } = true;

    /// <summary>The fields free text is searched in.</summary>
    public TextField TextFields { get; init; } = TextField.All;

    /// <summary>The order of a search without words that asks for none (filters only): the IMDb rating by default.</summary>
    public SortKey DefaultSort { get; init; } = SortKey.Imdb;
}

/// <summary>The result of a search: the documents in order, how the query was understood, and the order used.</summary>
public sealed record SearchOutcome(ParsedQuery Query, IReadOnlyList<int> Docs, bool FellBackToText, SortKey Sort);

/// <summary>Runs a parsed query on a <see cref="SearchIndex"/>.</summary>
public static class SearchEngine
{
    private const float NoMatch = -1f;

    private static readonly (TextField Field, float Weight)[] Weights =
    [
        (TextField.Name, 10f), (TextField.Original, 8f), (TextField.Series, 6f), (TextField.Actor, 6f), (TextField.Director, 6f),
        (TextField.Writer, 3f), (TextField.OtherPerson, 2f), (TextField.Genre, 4f), (TextField.Tag, 4f), (TextField.Studio, 2f), (TextField.Overview, 1.5f)
    ];

    /// <summary>Searches <paramref name="text"/>; when the phrases understood find nothing, the words are searched as plain text.</summary>
    public static SearchOutcome Search(SearchIndex index, string text, SearchContext context, bool naturalLanguage = true)
    {
        var parsed = QueryParser.Parse(text, index, naturalLanguage);
        var docs = Run(index, parsed, text, context);
        if (docs.Count == 0 && parsed.NaturalLanguage)
        {
            var literal = QueryParser.ParseLiteral(text, index);
            var literalDocs = Run(index, literal, text, context);
            if (literalDocs.Count > 0)
            {
                return new SearchOutcome(literal, literalDocs, true, EffectiveSort(literal, context.DefaultSort));
            }
        }

        return new SearchOutcome(parsed, docs, false, EffectiveSort(parsed, context.DefaultSort));
    }

    /// <summary>The order a query is listed in: its own, else relevance for words, else <paramref name="defaultSort"/>.</summary>
    public static SortKey EffectiveSort(ParsedQuery query, SortKey defaultSort)
        => query.Sort != SortKey.Relevance || query.HasText ? query.Sort : defaultSort == SortKey.Relevance ? SortKey.Imdb : defaultSort;

    /// <summary>Runs <paramref name="query"/>; <paramref name="text"/> is the query as typed, for the exact title bonus.</summary>
    public static List<int> Run(SearchIndex index, ParsedQuery query, string text, SearchContext context)
    {
        var n = index.Docs.Count;
        if (query.Root is null || n == 0)
        {
            return [];
        }

        var evaluator = new Evaluator(index, context);
        var scores = evaluator.Eval(query.Root);

        // a filter-only search does not list every episode, unless it asks for episodes
        var withEpisodes = query.HasText || AsksForEpisodes(query.Root);

        // the title typed exactly comes first, whatever the phrases understood ("Déjà vu", "Avant la nuit")
        var exact = TextFold.Fold(text);
        for (var i = 0; i < n; i++)
        {
            if (exact.Length > 0 && (index.NameFolded[i] == exact || index.OriginalFolded[i] == exact))
            {
                scores[i] = Math.Max(scores[i], 0) + 100f;
            }
            else if (scores[i] >= 0 && query.HasText && exact.Length > 2)
            {
                // the whole query in the title
                var padded = " " + index.NameFolded[i] + " ";
                if (padded.Contains(" " + exact + " ", StringComparison.Ordinal))
                {
                    scores[i] += index.NameFolded[i].StartsWith(exact, StringComparison.Ordinal) ? 15f : 8f;
                }
            }

            if (scores[i] >= 0 && index.Docs[i].Kind == DocKind.Episode)
            {
                if (!withEpisodes && scores[i] < 100f)
                {
                    scores[i] = NoMatch;
                }
                else
                {
                    scores[i] *= 0.85f;
                }
            }
        }

        var matched = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (scores[i] >= 0)
            {
                matched.Add(i);
            }
        }

        return Sort(index, matched, scores, EffectiveSort(query, context.DefaultSort), context).ToList();
    }

    private static bool AsksForEpisodes(QueryNode node) => node switch
    {
        FilterNode { Field.Id: FieldId.Type } f => f.Op != Op.NotEqual && f.Values.Any(v => v.Kind == DocKind.Episode),
        AndNode a => a.Children.Any(AsksForEpisodes),
        OrNode o => o.Children.Any(AsksForEpisodes),
        _ => false
    };

    private static IEnumerable<int> Sort(SearchIndex index, List<int> docs, float[] scores, SortKey sort, SearchContext context)
    {
        var d = index.Docs;
        double Imdb(int i) => d[i].ImdbId is { } id && context.Imdb(id) is { } r ? r.Rating : -1;

        // exact titles first in every order
        IOrderedEnumerable<int> ordered = docs.OrderByDescending(i => scores[i] >= 100f);
        ordered = sort switch
        {
            SortKey.Tmdb => ordered.ThenByDescending(i => d[i].CommunityRating ?? -1).ThenByDescending(i => d[i].Year ?? 0),
            SortKey.Imdb => ordered.ThenByDescending(Imdb).ThenByDescending(i => d[i].CommunityRating ?? -1),
            SortKey.YearNewest => ordered.ThenByDescending(i => d[i].Year ?? 0).ThenByDescending(i => d[i].DateCreated),
            SortKey.YearOldest => ordered.ThenBy(i => d[i].Year ?? 9999),
            SortKey.Added => ordered.ThenByDescending(i => d[i].DateCreated),
            SortKey.Title => ordered.ThenBy(i => d[i].SortName ?? d[i].Name, StringComparer.OrdinalIgnoreCase),
            SortKey.Runtime => ordered.ThenBy(i => d[i].RuntimeMinutes ?? double.MaxValue),
            SortKey.Critic => ordered.ThenByDescending(i => d[i].CriticRating ?? -1),
            SortKey.Random => ordered.ThenBy(i => HashCode.Combine(context.RandomSeed, d[i].Id)),
            _ => ordered.ThenByDescending(i => scores[i]).ThenByDescending(i => d[i].CommunityRating ?? -1).ThenByDescending(i => d[i].Year ?? 0)
        };
        return ordered.ThenBy(i => d[i].SortName ?? d[i].Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Evaluates the nodes of a query: one score per document, negative when it does not match.</summary>
    private sealed class Evaluator
    {
        private readonly SearchIndex _index;
        private readonly SearchContext _context;
        private HashSet<Guid>? _played;
        private HashSet<Guid>? _favorites;

        public Evaluator(SearchIndex index, SearchContext context)
        {
            _index = index;
            _context = context;
        }

        private int N => _index.Docs.Count;

        public float[] Eval(QueryNode node)
        {
            switch (node)
            {
                case AndNode and:
                {
                    var result = Filled(0f);
                    foreach (var child in and.Children)
                    {
                        var s = Eval(child);
                        for (var i = 0; i < N; i++)
                        {
                            result[i] = result[i] < 0 || s[i] < 0 ? NoMatch : result[i] + s[i];
                        }
                    }

                    return result;
                }

                case OrNode or:
                {
                    var result = Filled(NoMatch);
                    foreach (var child in or.Children)
                    {
                        var s = Eval(child);
                        for (var i = 0; i < N; i++)
                        {
                            if (s[i] >= 0)
                            {
                                result[i] = Math.Max(result[i], s[i]);
                            }
                        }
                    }

                    return result;
                }

                case NotNode not:
                {
                    var s = Eval(not.Child);
                    for (var i = 0; i < N; i++)
                    {
                        s[i] = s[i] >= 0 ? NoMatch : 0f;
                    }

                    return s;
                }

                case GenreWordNode g:
                    return Eval(new OrNode([g.Genre, g.Title]));
                case TextNode t:
                    return Term(t.Word, t.Fields);
                case PhraseNode p:
                    return Phrase(p.Phrase, p.Fields);
                case FilterNode f:
                    return Filter(f);
                default:
                    return Filled(NoMatch);
            }
        }

        private float[] Filled(float value)
        {
            var a = new float[N];
            Array.Fill(a, value);
            return a;
        }

        private float[] Term(string word, TextField fields)
        {
            var result = Filled(NoMatch);
            var free = fields == TextField.All;
            if (free)
            {
                fields = _context.TextFields | TextField.Titles;
            }

            foreach (var (token, quality) in _index.Expand(word, prefix: word.Length >= 2, fuzzy: _context.Typos))
            {
                foreach (var posting in _index.Postings(token))
                {
                    var field = posting.Field & fields;
                    if (field == TextField.None)
                    {
                        continue;
                    }

                    var w = Weight(field) * quality;
                    if (w > result[posting.Doc])
                    {
                        result[posting.Doc] = w;
                    }
                }
            }

            // a year typed alone ("1984", "2001") is also the year of the item
            if (free && word.Length == 4 && int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                for (var i = 0; i < N; i++)
                {
                    if (_index.Docs[i].Year == year)
                    {
                        result[i] = Math.Max(result[i], 5f);
                    }
                }
            }

            return result;
        }

        private float[] Phrase(string phrase, TextField fields)
        {
            var result = Filled(NoMatch);
            var needle = " " + phrase + " ";
            for (var i = 0; i < N; i++)
            {
                var best = NoMatch;
                if ((fields & TextField.Name) != 0 && (" " + _index.NameFolded[i] + " ").Contains(needle, StringComparison.Ordinal))
                {
                    best = 12f;
                }
                else if ((fields & TextField.Original) != 0 && (" " + _index.OriginalFolded[i] + " ").Contains(needle, StringComparison.Ordinal))
                {
                    best = 10f;
                }
                else if ((fields & TextField.Series) != 0 && (" " + _index.SeriesFolded[i] + " ").Contains(needle, StringComparison.Ordinal))
                {
                    best = 8f;
                }
                else if ((fields & TextField.Overview) != 0 && (" " + _index.OverviewFolded[i] + " ").Contains(needle, StringComparison.Ordinal))
                {
                    best = 3f;
                }
                else if ((fields & TextField.People) != 0 && _index.DocPeople[i].Any(p => (" " + p.Folded + " ").Contains(needle, StringComparison.Ordinal)))
                {
                    best = 6f;
                }

                result[i] = best;
            }

            return result;
        }

        private float[] Filter(FilterNode f)
        {
            var result = Filled(NoMatch);
            switch (f.Field.Kind)
            {
                case FieldKind.Text:
                    TextFilter(f, result);
                    break;
                case FieldKind.Number:
                    for (var i = 0; i < N; i++)
                    {
                        var value = NumberOf(f.Field.Id, i);
                        result[i] = f.Values.Any(v => Compare(f.Field.Id, f.Op, value, v)) ? 0f : NoMatch;
                    }

                    break;
                case FieldKind.Boolean:
                {
                    var wanted = f.Values.FirstOrDefault()?.Bool ?? true;
                    for (var i = 0; i < N; i++)
                    {
                        result[i] = BoolOf(f.Field.Id, i) == wanted ? 0f : NoMatch;
                    }

                    break;
                }

                case FieldKind.Kind:
                    for (var i = 0; i < N; i++)
                    {
                        var isKind = f.Values.Any(v => v.Kind == _index.Docs[i].Kind);
                        result[i] = isKind != (f.Op == Op.NotEqual) ? 0f : NoMatch;
                    }

                    break;
                case FieldKind.Added:
                    for (var i = 0; i < N; i++)
                    {
                        result[i] = f.Values.Any(v => CompareAdded(f.Op, _index.Docs[i].DateCreated, v)) ? 0f : NoMatch;
                    }

                    break;
                default:
                    Array.Fill(result, 0f);
                    break;
            }

            return result;
        }

        private void TextFilter(FilterNode f, float[] result)
        {
            var negate = f.Op == Op.NotEqual;
            var exact = f.Op is Op.Equal or Op.NotEqual;
            var any = new bool[N];
            foreach (var value in f.Values)
            {
                var text = value.Text ?? string.Empty;
                switch (f.Field.Id)
                {
                    case FieldId.Title:
                    case FieldId.Overview:
                    {
                        var fields = f.Field.Id == FieldId.Title ? TextField.Titles : TextField.Overview;
                        var words = TextFold.Tokens(text).Where(w => !TextFold.IsStopWord(w)).ToArray();
                        if (words.Length == 0)
                        {
                            words = TextFold.Tokens(text);
                        }

                        var scores = Filled(0f);
                        foreach (var w in words)
                        {
                            var s = Term(w, fields);
                            for (var i = 0; i < N; i++)
                            {
                                scores[i] = scores[i] < 0 || s[i] < 0 ? NoMatch : scores[i] + s[i];
                            }
                        }

                        for (var i = 0; i < N; i++)
                        {
                            if (scores[i] >= 0)
                            {
                                any[i] = true;
                                result[i] = Math.Max(result[i], scores[i]);
                            }
                        }

                        continue;
                    }

                    case FieldId.Actor:
                    case FieldId.Director:
                    case FieldId.Person:
                    {
                        var roles = f.Field.Id switch { FieldId.Actor => TextField.Actor, FieldId.Director => TextField.Director, _ => TextField.People };
                        var folded = TextFold.Fold(text);
                        var names = exact ? [folded] : _index.MatchPeople(text, roles);
                        for (var i = 0; i < N; i++)
                        {
                            if (_index.DocPeople[i].Any(p => (SearchIndex.RoleField(p.Role) & roles) != 0 && names.Contains(p.Folded)))
                            {
                                any[i] = true;
                            }
                        }

                        continue;
                    }

                    case FieldId.Media:
                    {
                        var folded = TextFold.Fold(text);
                        for (var i = 0; i < N; i++)
                        {
                            var media = _index.DocMediaId[i];
                            if (media is not null && (media == folded || (!exact && folded.Length >= 3 && media.StartsWith(folded, StringComparison.Ordinal))))
                            {
                                any[i] = true;
                            }
                        }

                        continue;
                    }

                    default:
                    {
                        var (vocabulary, values) = f.Field.Id switch
                        {
                            FieldId.Genre => (_index.Genres, _index.DocGenres),
                            FieldId.Tag => (_index.Tags, _index.DocTags),
                            _ => (_index.Studios, _index.DocStudios)
                        };
                        var wanted = SearchIndex.MatchValues(vocabulary, text, exact);
                        for (var i = 0; i < N; i++)
                        {
                            if (values[i].Any(wanted.Contains))
                            {
                                any[i] = true;
                            }
                        }

                        continue;
                    }
                }
            }

            for (var i = 0; i < N; i++)
            {
                var match = any[i] != negate;
                result[i] = match ? Math.Max(result[i], 0f) : NoMatch;
            }
        }

        private double? NumberOf(FieldId id, int i)
        {
            var d = _index.Docs[i];
            return id switch
            {
                FieldId.Year => d.Year,
                FieldId.Runtime => d.RuntimeMinutes,
                FieldId.Tmdb => d.CommunityRating,
                FieldId.Critic => d.CriticRating,
                FieldId.Imdb => d.ImdbId is { } imdb && _context.Imdb(imdb) is { } r ? r.Rating : null,
                FieldId.ImdbVotes => d.ImdbId is { } imdb2 && _context.Imdb(imdb2) is { } r2 ? r2.Votes : null,
                FieldId.Age => AgeOf(d.OfficialRating),
                _ => null
            };
        }

        /// <summary>The minimum age of an official rating: "FR-12" → 12, "PG-13" → 13, "TP" / "G" / "U" → 0.</summary>
        internal static double? AgeOf(string? rating)
        {
            if (string.IsNullOrWhiteSpace(rating))
            {
                return null;
            }

            var digits = new string(rating.Where(char.IsDigit).ToArray());
            if (digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var age))
            {
                return age;
            }

            var r = rating.Trim().ToUpperInvariant();
            if (r.EndsWith("TP", StringComparison.Ordinal) || r.EndsWith("-U", StringComparison.Ordinal) || r is "U" or "G" or "TV-G" or "TV-Y" or "ALL" or "AL" or "0")
            {
                return 0;
            }

            return r switch { "PG" or "TV-PG" => 10, "R" or "TV-MA" or "NC-17" => 17, _ => null };
        }

        private bool BoolOf(FieldId id, int i)
        {
            var d = _index.Docs[i];
            switch (id)
            {
                case FieldId.Watched:
                    _played ??= _context.Played();
                    return _played.Contains(d.Id);
                case FieldId.Favorite:
                    _favorites ??= _context.Favorites();
                    return _favorites.Contains(d.Id);
                default:
                    // not on a USB drive: always playable
                    return d.MediaId is null || _context.IsMediaPresent(d.MediaId);
            }
        }

        private static bool Compare(FieldId id, Op op, double? value, FilterValue wanted)
        {
            if (value is not { } v || wanted.Number is not { } n)
            {
                return op == Op.NotEqual && value is null;
            }

            if (wanted.High is { } high)
            {
                var inRange = v >= n && v <= high;
                return op == Op.NotEqual ? !inRange : op switch
                {
                    Op.Greater => v > high,
                    Op.GreaterOrEqual => v >= n,
                    Op.Less => v < n,
                    Op.LessOrEqual => v <= high,
                    _ => inRange
                };
            }

            return op switch
            {
                Op.Greater => v > n,
                Op.GreaterOrEqual => v >= n,
                Op.Less => v < n,
                Op.LessOrEqual => v <= n,
                Op.Equal => Math.Abs(v - n) < 0.05,
                Op.NotEqual => Math.Abs(v - n) >= 0.05,
                _ => id switch
                {
                    // ":" means "about": a rating of 7 is 7 to 7.9, a runtime of 90 is 85 to 95 minutes
                    FieldId.Tmdb or FieldId.Imdb => v >= n && v < n + 1,
                    FieldId.Critic => v >= n - 5 && v <= n + 5,
                    FieldId.Runtime => Math.Abs(v - n) <= 5,
                    FieldId.ImdbVotes => v >= n,
                    _ => Math.Abs(v - n) < 0.05
                }
            };
        }

        private bool CompareAdded(Op op, DateTime created, FilterValue wanted)
        {
            if (wanted.Days is { } days)
            {
                var age = (_context.Now - created).TotalDays;
                return op switch
                {
                    Op.Greater => age > days,
                    Op.GreaterOrEqual => age >= days,
                    Op.Less => age < days,
                    Op.Equal => Math.Abs(age - days) < 1,
                    Op.NotEqual => Math.Abs(age - days) >= 1,
                    _ => age <= days
                };
            }

            if (wanted.Date is { } date)
            {
                return op switch
                {
                    Op.Greater => created > date,
                    Op.Less => created < date,
                    Op.LessOrEqual => created <= date,
                    Op.NotEqual => created < date,
                    _ => created >= date
                };
            }

            return false;
        }

        private static float Weight(TextField field)
        {
            var best = 0f;
            foreach (var (f, w) in Weights)
            {
                if ((field & f) != 0 && w > best)
                {
                    best = w;
                }
            }

            return best;
        }
    }
}
