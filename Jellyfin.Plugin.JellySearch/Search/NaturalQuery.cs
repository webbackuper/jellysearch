using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>
/// Reads a search typed or dictated as a phrase, in French (and a little English):
/// "films d'horreur des années 80 avec Sigourney Weaver", "comédies notées plus de 7 sur IMDb",
/// "séries non vues de moins de 45 minutes", "films de Spielberg après 2000 disponibles".
/// Genres, people and MediaIDs are only recognised when the library has them, so that a title such as
/// "Danse avec les loups" stays a title. Whatever is not understood stays free text.
/// </summary>
public static class NaturalQuery
{
    /// <summary>The outcome: the nodes (filters and words), whether something was understood, a suggested order.</summary>
    public sealed record Reading(List<QueryNode> Nodes, bool Understood, SortKey? Sort);

    private static readonly string[][] Fillers =
    [
        ["je", "veux", "voir"], ["je", "veux"], ["je", "voudrais", "voir"], ["je", "voudrais"], ["j", "aimerais", "voir"], ["j", "aimerais"],
        ["montre", "moi"], ["montrez", "moi"], ["affiche", "moi"], ["affiche"], ["trouve", "moi"], ["trouve"], ["donne", "moi"],
        ["cherche"], ["recherche"], ["mets", "moi"], ["propose", "moi"], ["s", "il", "te", "plait"], ["s", "il", "vous", "plait"], ["stp"],
        ["show", "me"], ["find", "me"], ["find"], ["i", "want", "to", "watch"], ["i", "want"], ["please"]
    ];

    // words that only join the others in a phrase
    private static readonly HashSet<string> Glue = new(StringComparer.Ordinal)
    {
        "des", "les", "la", "le", "l", "d", "de", "du", "un", "une", "avec", "et", "qui", "que", "sont", "je", "n", "ai", "me", "moi",
        "mes", "a", "en", "sur", "dans", "the", "of", "some", "all", "tous", "toutes", "quelques", "sorti", "sortis", "sortie", "sorties",
        "dure", "durent", "durant", "ont", "avoir", "of", "from", "with", "in", "that", "are", "is", "pour", "par", "ou", "or", "and", "y", "ceux", "celles"
    };

    private static readonly (string[] Words, Op Op)[] Comparators =
    [
        (["superieure", "ou", "egale", "a"], Op.GreaterOrEqual), (["superieur", "ou", "egal", "a"], Op.GreaterOrEqual),
        (["inferieure", "ou", "egale", "a"], Op.LessOrEqual), (["inferieur", "ou", "egal", "a"], Op.LessOrEqual),
        (["pas", "plus", "de"], Op.LessOrEqual), (["au", "dessus", "de"], Op.Greater), (["en", "dessous", "de"], Op.Less),
        (["superieure", "a"], Op.Greater), (["superieur", "a"], Op.Greater), (["superieures", "a"], Op.Greater), (["superieurs", "a"], Op.Greater),
        (["inferieure", "a"], Op.Less), (["inferieur", "a"], Op.Less), (["inferieures", "a"], Op.Less), (["inferieurs", "a"], Op.Less),
        (["plus", "de"], Op.Greater), (["moins", "de"], Op.Less), (["au", "moins"], Op.GreaterOrEqual), (["au", "plus"], Op.LessOrEqual),
        (["a", "partir", "de"], Op.GreaterOrEqual), (["minimum"], Op.GreaterOrEqual), (["maximum"], Op.LessOrEqual), (["jusqu", "a"], Op.LessOrEqual),
        (["more", "than"], Op.Greater), (["less", "than"], Op.Less), (["at", "least"], Op.GreaterOrEqual), (["at", "most"], Op.LessOrEqual),
        (["over"], Op.Greater), (["above"], Op.Greater), (["under"], Op.Less), (["below"], Op.Less)
    ];

    private static readonly HashSet<string> MinuteUnits = new(StringComparer.Ordinal) { "minutes", "minute", "min", "mn", "mins" };
    private static readonly HashSet<string> HourUnits = new(StringComparer.Ordinal) { "heures", "heure", "h", "hours", "hour", "hrs" };
    private static readonly HashSet<string> RatingWords = new(StringComparer.Ordinal) { "note", "notes", "notee", "notees", "rating", "rated", "noter", "notation" };

    // genre aliases: what is said → the genre names libraries usually have (French or English), folded
    private static readonly (string[] Said, string[] Genres)[] GenreAliases =
    [
        (["sf", "science fiction", "sci fi", "scifi", "science-fiction"], ["science fiction", "sci fi & fantasy", "science-fiction"]),
        (["horreur", "epouvante", "horror", "films d horreur"], ["horreur", "horror", "epouvante"]),
        (["comedie", "comedy", "comique", "drole", "humour"], ["comedie", "comedy"]),
        (["polar", "policier", "crime", "criminel", "gangster", "mafia"], ["crime", "policier", "polar"]),
        (["dessin anime", "animation", "anime", "cartoon", "dessins animes"], ["animation"]),
        (["documentaire", "docu", "documentary"], ["documentaire", "documentary"]),
        (["romance", "romantique", "amour", "romantic"], ["romance"]),
        (["guerre", "war"], ["guerre", "war", "war & politics", "guerre & politique"]),
        (["fantastique", "fantasy", "fantaisie"], ["fantastique", "fantasy"]),
        (["aventure", "adventure"], ["aventure", "adventure", "action & adventure", "action & aventure"]),
        (["action"], ["action", "action & adventure", "action & aventure"]),
        (["thriller", "suspense"], ["thriller", "suspense"]),
        (["western"], ["western"]),
        (["musical", "comedie musicale", "musique", "music"], ["musique", "music", "musical"]),
        (["familial", "famille", "family", "enfant", "enfants"], ["familial", "family", "kids", "enfants"]),
        (["historique", "history", "histoire"], ["histoire", "history"]),
        (["mystere", "mystery", "enigme"], ["mystere", "mystery"]),
        (["drame", "drama", "dramatique"], ["drame", "drama"]),
        (["telefilm", "tv movie"], ["telefilm", "tv movie"]),
        (["biopic", "biographie", "biographique", "biography"], ["biographie", "biography", "biopic"]),
        (["sport", "sports", "sportif"], ["sport"])
    ];

    /// <summary>Reads <paramref name="tokens"/> (folded words).</summary>
    public static Reading Read(IReadOnlyList<string> tokens, SearchIndex index)
    {
        var nodes = new List<QueryNode>();
        var words = new List<string>();
        var understood = false;
        SortKey? sort = null;
        var i = 0;

        // "je veux voir", "montre-moi"… at the start
        foreach (var filler in Fillers)
        {
            if (Seq(tokens, 0, filler))
            {
                i = filler.Length;
                understood = true;
                break;
            }
        }

        var orAfter = -1; // "comédies ou drames": the node before "ou", to join with the next one
        while (i < tokens.Count)
        {
            var before = nodes.Count;
            var used = TryRule(tokens, i, index, nodes, ref sort);
            if (used > 0)
            {
                understood = true;
                i += used;
                if (orAfter >= 0 && orAfter == before - 1 && nodes.Count == before + 1)
                {
                    var joined = new OrNode([nodes[orAfter], nodes[before]]);
                    nodes.RemoveRange(orAfter, 2);
                    nodes.Insert(orAfter, joined);
                }

                orAfter = -1;
                continue;
            }

            if ((tokens[i] == "ou" || tokens[i] == "or") && nodes.Count > 0 && words.Count == 0)
            {
                orAfter = nodes.Count - 1;
                i++;
                continue;
            }

            orAfter = -1;
            words.Add(tokens[i]);
            i++;
        }

        if (!understood)
        {
            return new Reading(tokens.Select(t => (QueryNode)new TextNode(t)).ToList(), false, null);
        }

        // what is left: free text, without the words that only join the phrase
        foreach (var w in words)
        {
            if (!Glue.Contains(w))
            {
                nodes.Add(new TextNode(w));
            }
        }

        return new Reading(nodes, true, sort);
    }

    private static int TryRule(IReadOnlyList<string> t, int i, SearchIndex index, List<QueryNode> nodes, ref SortKey? sort)
    {
        int used;
        if ((used = Watched(t, i, nodes)) > 0
            || (used = Recent(t, i, nodes, ref sort)) > 0
            || (used = SortWords(t, i, ref sort)) > 0
            || (used = Decade(t, i, nodes)) > 0
            || (used = YearWords(t, i, nodes)) > 0
            || (used = RuntimeOrRating(t, i, nodes, ref sort)) > 0
            || (used = Kind(t, i, nodes)) > 0
            || (used = Available(t, i, nodes)) > 0
            || (used = Media(t, i, index, nodes)) > 0
            || (used = People(t, i, index, nodes)) > 0
            || (used = Genre(t, i, index, nodes)) > 0)
        {
            return used;
        }

        return 0;
    }

    private static int Watched(IReadOnlyList<string> t, int i, List<QueryNode> nodes)
    {
        string[][] notSeen = [["que", "je", "n", "ai", "pas", "vus"], ["que", "je", "n", "ai", "pas", "vu"], ["pas", "encore", "vus"], ["pas", "encore", "vu"],
            ["non", "vus"], ["non", "vu"], ["non", "vues"], ["pas", "vus"], ["pas", "vu"], ["pas", "vues"], ["jamais", "vus"], ["jamais", "vu"],
            ["a", "voir"], ["unwatched"], ["unseen"], ["not", "watched"], ["jamais", "regardes"], ["pas", "regardes"]];
        foreach (var p in notSeen)
        {
            if (Seq(t, i, p))
            {
                nodes.Add(Filter(FieldId.Watched, Op.Match, new FilterValue { Bool = false }));
                return p.Length;
            }
        }

        string[][] seen = [["deja", "vus"], ["deja", "vues"], ["deja", "regardes"], ["watched"]];
        foreach (var p in seen)
        {
            if (Seq(t, i, p))
            {
                nodes.Add(Filter(FieldId.Watched, Op.Match, new FilterValue { Bool = true }));
                return p.Length;
            }
        }

        string[][] favorite = [["mes", "favoris"], ["en", "favoris"], ["favoris"], ["favorites"], ["favourites"], ["preferes"], ["mes", "preferes"]];
        foreach (var p in favorite)
        {
            if (Seq(t, i, p))
            {
                nodes.Add(Filter(FieldId.Favorite, Op.Match, new FilterValue { Bool = true }));
                return p.Length;
            }
        }

        return 0;
    }

    private static int Recent(IReadOnlyList<string> t, int i, List<QueryNode> nodes, ref SortKey? sort)
    {
        (string[] Words, double Days)[] recent =
        [
            (["ajoutes", "cette", "semaine"], 7), (["cette", "semaine"], 7), (["ajoutes", "ce", "mois"], 31), (["ce", "mois", "ci"], 31), (["ce", "mois"], 31),
            (["ajoutes", "cette", "annee"], 365), (["recemment", "ajoutes"], 30), (["ajoutes", "recemment"], 30), (["derniers", "ajouts"], 30),
            (["dernieres", "nouveautes"], 30), (["nouveautes"], 30), (["recently", "added"], 30), (["recents"], 30), (["recentes"], 30)
        ];
        foreach (var (words, days) in recent)
        {
            if (Seq(t, i, words))
            {
                nodes.Add(Filter(FieldId.Added, Op.Less, new FilterValue { Days = days }));
                sort ??= SortKey.Added;
                return words.Length;
            }
        }

        return 0;
    }

    private static int SortWords(IReadOnlyList<string> t, int i, ref SortKey? sort)
    {
        (string[] Words, SortKey Key)[] sorts =
        [
            (["les", "mieux", "notes"], SortKey.Tmdb), (["mieux", "notes"], SortKey.Tmdb), (["tries", "par", "note"], SortKey.Tmdb), (["par", "note"], SortKey.Tmdb),
            (["les", "plus", "recents"], SortKey.YearNewest), (["plus", "recents"], SortKey.YearNewest), (["les", "plus", "anciens"], SortKey.YearOldest), (["plus", "anciens"], SortKey.YearOldest),
            (["les", "plus", "courts"], SortKey.Runtime), (["plus", "courts"], SortKey.Runtime), (["au", "hasard"], SortKey.Random), (["aleatoire"], SortKey.Random),
            (["par", "ordre", "alphabetique"], SortKey.Title), (["ordre", "alphabetique"], SortKey.Title), (["top", "rated"], SortKey.Tmdb), (["best", "rated"], SortKey.Tmdb)
        ];
        foreach (var (words, key) in sorts)
        {
            if (Seq(t, i, words))
            {
                sort = key;
                return words.Length;
            }
        }

        return 0;
    }

    private static int Decade(IReadOnlyList<string> t, int i, List<QueryNode> nodes)
    {
        // "années 80", "annees 1980", "années 2000", "80s", "les 90s"
        if ((t[i] == "annees" || t[i] == "annee" || t[i] == "the") && i + 1 < t.Count && DecadeStart(t[i + 1].TrimEnd('s')) is { } d)
        {
            nodes.Add(Filter(FieldId.Year, Op.Match, new FilterValue { Number = d, High = d + 9 }));
            return 2;
        }

        if (t[i].Length is 3 or 5 && t[i].EndsWith('s') && DecadeStart(t[i][..^1]) is { } d2)
        {
            nodes.Add(Filter(FieldId.Year, Op.Match, new FilterValue { Number = d2, High = d2 + 9 }));
            return 1;
        }

        return 0;
    }

    private static int? DecadeStart(string digits)
    {
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            return null;
        }

        if (digits.Length == 2 && n % 10 == 0)
        {
            return n + (n <= 30 ? 2000 : 1900);
        }

        return digits.Length == 4 && n % 10 == 0 && n is >= 1900 and <= 2090 ? n : null;
    }

    private static int YearWords(IReadOnlyList<string> t, int i, List<QueryNode> nodes)
    {
        // "entre 1990 et 2000"
        if ((t[i] == "entre" || t[i] == "between") && i + 3 < t.Count && Year(t[i + 1]) is { } a && (t[i + 2] == "et" || t[i + 2] == "and") && Year(t[i + 3]) is { } b)
        {
            nodes.Add(Filter(FieldId.Year, Op.Match, new FilterValue { Number = Math.Min(a, b), High = Math.Max(a, b) }));
            return 4;
        }

        (string[] Words, Op Op)[] preps =
        [
            (["a", "partir", "de"], Op.GreaterOrEqual), (["depuis"], Op.GreaterOrEqual), (["since"], Op.GreaterOrEqual),
            (["apres"], Op.Greater), (["after"], Op.Greater), (["avant"], Op.Less), (["before"], Op.Less),
            (["sortis", "en"], Op.Equal), (["sorti", "en"], Op.Equal), (["sortie", "en"], Op.Equal), (["sorties", "en"], Op.Equal),
            (["de"], Op.Equal), (["en"], Op.Equal), (["in"], Op.Equal), (["from"], Op.Equal), (["annee"], Op.Equal)
        ];
        foreach (var (words, op) in preps)
        {
            if (Seq(t, i, words) && i + words.Length < t.Count && Year(t[i + words.Length]) is { } y)
            {
                nodes.Add(Filter(FieldId.Year, op, new FilterValue { Number = y }));
                return words.Length + 1;
            }
        }

        return 0;
    }

    private static int? Year(string token)
        => token.Length == 4 && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1900 and <= 2099 ? y : null;

    private static int RuntimeOrRating(IReadOnlyList<string> t, int i, List<QueryNode> nodes, ref SortKey? sort)
    {
        // "bien notés"
        string[][] wellRated = [["tres", "bien", "notes"], ["bien", "notes"], ["bien", "note"], ["bien", "notees"], ["bons", "films"], ["good"]];
        foreach (var p in wellRated)
        {
            if (Seq(t, i, p))
            {
                nodes.Add(Filter(FieldId.Tmdb, Op.GreaterOrEqual, new FilterValue { Number = p.Length == 3 ? 8 : 7 }));
                sort ??= SortKey.Tmdb;
                return p.Length;
            }
        }

        // [note | noté | qui durent…] [imdb|tmdb] comparator N [unit]
        var j = i;
        var field = (FieldId?)null;
        var ratingWord = false;
        while (j < t.Count && (RatingWords.Contains(t[j]) || t[j] is "avec" or "une" or "qui" or "durent" or "dure" or "duree" or "de" or "imdb" or "tmdb" or "sur"))
        {
            ratingWord |= RatingWords.Contains(t[j]);
            if (t[j] == "imdb")
            {
                field = FieldId.Imdb;
            }
            else if (t[j] == "tmdb")
            {
                field = FieldId.Tmdb;
            }

            j++;
        }

        var op = Op.GreaterOrEqual;
        var cmp = Comparators.FirstOrDefault(c => Seq(t, j, c.Words));
        var hasComparator = cmp.Words is not null;
        if (hasComparator)
        {
            op = cmp.Op;
            j += cmp.Words!.Length;
        }

        if (j >= t.Count || (!hasComparator && !ratingWord && field is null))
        {
            return 0;
        }

        // the number, with its unit attached ("2h", "1h30", "90min") or after it
        if (QueryParser.ParseMinutes(t[j]) is { } attached && !t[j].All(c => char.IsDigit(c) || c == '.'))
        {
            nodes.Add(Filter(FieldId.Runtime, op, new FilterValue { Number = attached }));
            return j + 1 - i;
        }

        if (!TextFold.TryNumber(t[j], out var n))
        {
            return 0;
        }

        var k = j + 1;
        if (k < t.Count && MinuteUnits.Contains(t[k]))
        {
            nodes.Add(Filter(FieldId.Runtime, op, new FilterValue { Number = n }));
            return k + 1 - i;
        }

        if (k < t.Count && HourUnits.Contains(t[k]))
        {
            var minutes = n * 60;
            if (k + 1 < t.Count && t[k + 1] == "et" && k + 2 < t.Count && t[k + 2] == "demi")
            {
                minutes += 30;
                k += 2;
            }

            nodes.Add(Filter(FieldId.Runtime, op, new FilterValue { Number = minutes }));
            return k + 1 - i;
        }

        // a rating: "7", "7.5", "sept", then "sur 10", "étoiles", "sur imdb"
        if (n > 10 || (!hasComparator && !ratingWord && field is null))
        {
            return 0;
        }

        while (k < t.Count && (t[k] is "sur" or "10" or "etoiles" or "etoile" or "stars" or "imdb" or "tmdb" or "points"))
        {
            if (t[k] == "imdb")
            {
                field = FieldId.Imdb;
            }
            else if (t[k] == "tmdb")
            {
                field = FieldId.Tmdb;
            }

            k++;
        }

        nodes.Add(Filter(field ?? FieldId.Tmdb, op, new FilterValue { Number = n }));
        return k - i;
    }

    private static int Kind(IReadOnlyList<string> t, int i, List<QueryNode> nodes)
    {
        if (Seq(t, i, ["long", "metrage"]) || Seq(t, i, ["longs", "metrages"]))
        {
            nodes.Add(Filter(FieldId.Type, Op.Match, new FilterValue { Kind = DocKind.Movie }));
            return 2;
        }

        var kind = t[i] switch
        {
            "film" or "films" or "movie" or "movies" => DocKind.Movie,
            "serie" or "series" or "feuilleton" or "feuilletons" or "show" or "shows" => DocKind.Series,
            "episode" or "episodes" => DocKind.Episode,
            "collection" or "collections" or "saga" or "sagas" => (DocKind?)DocKind.BoxSet,
            _ => null
        };
        if (kind is null)
        {
            return 0;
        }

        nodes.Add(Filter(FieldId.Type, Op.Match, new FilterValue { Kind = kind }));
        return 1;
    }

    private static int Available(IReadOnlyList<string> t, int i, List<QueryNode> nodes)
    {
        string[][] yes = [["disponibles", "maintenant"], ["disponibles"], ["disponible"], ["dispo"], ["branches"], ["lisibles"], ["available"]];
        string[][] no = [["non", "disponibles"], ["indisponibles"], ["absents"], ["debranches"], ["unavailable"]];
        foreach (var p in no)
        {
            if (Seq(t, i, p))
            {
                nodes.Add(Filter(FieldId.Available, Op.Match, new FilterValue { Bool = false }));
                return p.Length;
            }
        }

        foreach (var p in yes)
        {
            if (Seq(t, i, p))
            {
                nodes.Add(Filter(FieldId.Available, Op.Match, new FilterValue { Bool = true }));
                return p.Length;
            }
        }

        return 0;
    }

    private static int Media(IReadOnlyList<string> t, int i, SearchIndex index, List<QueryNode> nodes)
    {
        // "sur le disque MOVIES03", "support MOVIES03", or the MediaID alone
        var j = i;
        while (j < t.Count - 1 && t[j] is "sur" or "le" or "disque" or "support" or "media" or "mediaid" or "du" or "on" or "the" or "drive" or "disk")
        {
            j++;
        }

        if (j < t.Count && index.MediaIds.TryGetValue(t[j], out var mediaId))
        {
            nodes.Add(Filter(FieldId.Media, Op.Equal, new FilterValue { Text = mediaId }));
            return j + 1 - i;
        }

        return 0;
    }

    private static int People(IReadOnlyList<string> t, int i, SearchIndex index, List<QueryNode> nodes)
    {
        // "avec X Y", "de X", "réalisé par X Y"
        (string[] Words, FieldId Field)[] intros =
        [
            (["realise", "par"], FieldId.Director), (["realises", "par"], FieldId.Director), (["realisee", "par"], FieldId.Director), (["realisees", "par"], FieldId.Director),
            (["realisateur"], FieldId.Director), (["directed", "by"], FieldId.Director), (["par"], FieldId.Director), (["de"], FieldId.Director), (["by"], FieldId.Director),
            (["avec"], FieldId.Actor), (["with"], FieldId.Actor), (["starring"], FieldId.Actor), (["joue", "par"], FieldId.Actor), (["interprete", "par"], FieldId.Actor)
        ];
        foreach (var (words, field) in intros)
        {
            if (!Seq(t, i, words))
            {
                continue;
            }

            var start = i + words.Length;

            // "de" and "par" are common words: "films de guerre" is a genre, and a single name must be exact
            var weak = words.Length == 1 && words[0] is "de" or "par" or "by";
            if (weak && start < t.Count && Genre(t, start, index, []) > 0)
            {
                continue;
            }

            for (var len = Math.Min(3, t.Count - start); len >= 1; len--)
            {
                var name = string.Join(' ', t.Skip(start).Take(len));
                if (len == 1 && (name.Length < 3 || TextFold.IsStopWord(name)))
                {
                    continue;
                }

                var roles = field == FieldId.Director ? TextField.Director : TextField.Actor;
                var found = index.MatchPeople(name, roles, strict: weak && len == 1);
                if (found.Count > 0)
                {
                    nodes.Add(Filter(field, Op.Match, new FilterValue { Text = Display(index, found, name) }));
                    return words.Length + len;
                }

                // "avec Spielberg": a director named after "avec" is still someone of the movie
                found = field == FieldId.Actor ? index.MatchPeople(name, TextField.People) : [];
                if (found.Count > 0)
                {
                    nodes.Add(Filter(FieldId.Person, Op.Match, new FilterValue { Text = Display(index, found, name) }));
                    return words.Length + len;
                }
            }
        }

        return 0;
    }

    private static int Genre(IReadOnlyList<string> t, int i, SearchIndex index, List<QueryNode> nodes)
    {
        if (index.Genres.Count == 0)
        {
            return 0;
        }

        for (var len = Math.Min(3, t.Count - i); len >= 1; len--)
        {
            var said = string.Join(' ', t.Skip(i).Take(len));
            var singular = string.Join(' ', t.Skip(i).Take(len).Select(TextFold.Singular));
            if (len == 1 && (said.Length < 3 || TextFold.IsStopWord(said)))
            {
                continue;
            }

            var genres = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (aliases, targets) in GenreAliases)
            {
                if (aliases.Contains(said) || aliases.Contains(singular))
                {
                    foreach (var target in targets)
                    {
                        if (index.Genres.ContainsKey(target))
                        {
                            genres.Add(target);
                        }
                    }
                }
            }

            foreach (var g in index.Genres.Keys)
            {
                if (g == said || g == singular || string.Join(' ', g.Split(' ').Select(TextFold.Singular)) == singular)
                {
                    genres.Add(g);
                }
            }

            if (genres.Count == 0)
            {
                continue;
            }

            // the genre, or the word in a title ("Action Jackson", "Comédie de l'amour")
            var filter = new FilterNode(Fields.Get(FieldId.Genre), Op.Equal, genres.OrderBy(g => g, StringComparer.Ordinal).Select(g => new FilterValue { Text = index.Genres[g] }).ToList());
            QueryNode title = len == 1 ? new TextNode(said, TextField.Name) : new PhraseNode(said, TextField.Titles);
            nodes.Add(new GenreWordNode(filter, title));
            return len;
        }

        return 0;
    }

    // one person found: their name as written in the library ("Jim Carrey" rather than "jim carrey")
    private static string Display(SearchIndex index, HashSet<string> found, string said)
        => found.Count == 1 && index.PeopleNames.TryGetValue(found.First(), out var p) ? p.Display : said;

    private static bool Seq(IReadOnlyList<string> t, int i, string[] words)
    {
        if (i + words.Length > t.Count)
        {
            return false;
        }

        for (var k = 0; k < words.Length; k++)
        {
            if (t[i + k] != words[k])
            {
                return false;
            }
        }

        return true;
    }

    private static FilterNode Filter(FieldId id, Op op, FilterValue value) => new(Fields.Get(id), op, [value]);
}
