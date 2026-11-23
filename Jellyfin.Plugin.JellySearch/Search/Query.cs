using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>What a filter looks at.</summary>
public enum FieldId
{
    Title,
    Overview,
    Actor,
    Director,
    Person,
    Genre,
    Tag,
    Studio,
    Year,
    Runtime,
    Tmdb,
    Imdb,
    ImdbVotes,
    Critic,
    Age,
    Media,
    Available,
    Type,
    Watched,
    Favorite,
    Added,
    Sort
}

/// <summary>How a filter compares.</summary>
public enum Op
{
    /// <summary>":" — contains, or about equal for numbers.</summary>
    Match,
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual
}

/// <summary>The kind of values a field takes.</summary>
public enum FieldKind
{
    Text,
    Number,
    Boolean,
    Kind,
    Added,
    Sort
}

/// <summary>A field of the query language, with its names in French and English.</summary>
public sealed record FieldInfo(FieldId Id, FieldKind Kind, string Label, string Syntax, string[] Names);

/// <summary>The fields of the query language.</summary>
public static class Fields
{
    public static readonly IReadOnlyList<FieldInfo> All =
    [
        new(FieldId.Title, FieldKind.Text, "titre", "titre", ["titre", "title", "nom", "name"]),
        new(FieldId.Overview, FieldKind.Text, "résumé", "resume", ["resume", "synopsis", "overview", "plot", "intrigue", "histoire"]),
        new(FieldId.Actor, FieldKind.Text, "acteur", "acteur", ["acteur", "acteurs", "actrice", "actrices", "actor", "actors", "avec", "with", "cast", "casting"]),
        new(FieldId.Director, FieldKind.Text, "réalisateur", "realisateur", ["realisateur", "realisatrice", "realisateurs", "real", "director", "directeur", "par", "by"]),
        new(FieldId.Person, FieldKind.Text, "personne", "personne", ["personne", "personnes", "person", "people", "equipe"]),
        new(FieldId.Genre, FieldKind.Text, "genre", "genre", ["genre", "genres"]),
        new(FieldId.Tag, FieldKind.Text, "tag", "tag", ["tag", "tags", "motcle", "motscles", "keyword", "keywords"]),
        new(FieldId.Studio, FieldKind.Text, "studio", "studio", ["studio", "studios", "production"]),
        new(FieldId.Year, FieldKind.Number, "année", "annee", ["annee", "annees", "an", "year", "years", "sortie"]),
        new(FieldId.Runtime, FieldKind.Number, "durée", "duree", ["duree", "runtime", "longueur", "temps", "length", "minutes"]),
        new(FieldId.Tmdb, FieldKind.Number, "note TMDB", "tmdb", ["tmdb", "note", "notes", "rating", "communaute", "community"]),
        new(FieldId.Imdb, FieldKind.Number, "note IMDb", "imdb", ["imdb"]),
        new(FieldId.ImdbVotes, FieldKind.Number, "votes IMDb", "votes", ["votes", "imdbvotes", "vote"]),
        new(FieldId.Critic, FieldKind.Number, "note critique", "critique", ["critique", "critiques", "critic", "critics", "rt", "rotten", "presse"]),
        new(FieldId.Age, FieldKind.Number, "âge minimum", "age", ["age", "classification", "pegi", "csa", "rated", "certification"]),
        new(FieldId.Media, FieldKind.Text, "MediaID", "media", ["media", "mediaid", "disque", "support", "usb"]),
        new(FieldId.Available, FieldKind.Boolean, "disponible", "dispo", ["dispo", "disponible", "disponibles", "branche", "available", "lisible"]),
        new(FieldId.Type, FieldKind.Kind, "type", "type", ["type", "kind"]),
        new(FieldId.Watched, FieldKind.Boolean, "vu", "vu", ["vu", "vus", "vue", "vues", "watched", "played", "visionne"]),
        new(FieldId.Favorite, FieldKind.Boolean, "favori", "favori", ["favori", "favoris", "favorite", "favorites", "fav", "prefere"]),
        new(FieldId.Added, FieldKind.Added, "ajouté", "ajout", ["ajout", "ajoute", "ajoutes", "added", "nouveau"]),
        new(FieldId.Sort, FieldKind.Sort, "tri", "tri", ["tri", "trier", "sort", "ordre", "order"])
    ];

    private static readonly Dictionary<string, FieldInfo> ByName = All
        .SelectMany(f => f.Names.Select(n => (n, f)))
        .ToDictionary(x => x.n, x => x.f, StringComparer.Ordinal);

    /// <summary>The field named <paramref name="name"/> (any case, accents or not).</summary>
    public static FieldInfo? Find(string name) => ByName.TryGetValue(TextFold.Fold(name).Replace(" ", string.Empty, StringComparison.Ordinal), out var f) ? f : null;

    public static FieldInfo Get(FieldId id) => All.First(f => f.Id == id);
}

/// <summary>A sort order asked with "tri:".</summary>
public enum SortKey
{
    Relevance,
    Tmdb,
    Imdb,
    YearNewest,
    YearOldest,
    Added,
    Title,
    Runtime,
    Critic,
    Random
}

/// <summary>A node of a parsed query.</summary>
public abstract record QueryNode
{
    /// <summary>The node in the query language (what the web page shows and saves).</summary>
    public abstract string ToSyntax();

    /// <summary>The node in plain French, for "Compris : …".</summary>
    public abstract string Describe();

    internal static string Quote(string value)
        => value.Any(c => char.IsWhiteSpace(c) || c is '(' or ')' or '|' or ',' or ':' or '"') ? "\"" + value.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"" : value;
}

/// <summary>Every child must match.</summary>
public sealed record AndNode(IReadOnlyList<QueryNode> Children) : QueryNode
{
    public override string ToSyntax() => string.Join(' ', Children.Select(c => c is OrNode ? "(" + c.ToSyntax() + ")" : c.ToSyntax()));

    public override string Describe() => string.Join(" · ", Children.Select(c => c is OrNode ? "(" + c.Describe() + ")" : c.Describe()));
}

/// <summary>One child at least must match.</summary>
public sealed record OrNode(IReadOnlyList<QueryNode> Children) : QueryNode
{
    public override string ToSyntax() => string.Join(" OU ", Children.Select(c => c is AndNode ? "(" + c.ToSyntax() + ")" : c.ToSyntax()));

    public override string Describe() => string.Join(" ou ", Children.Select(c => c is AndNode ? "(" + c.Describe() + ")" : c.Describe()));
}

/// <summary>The child must not match.</summary>
public sealed record NotNode(QueryNode Child) : QueryNode
{
    public override string ToSyntax() => Child is TextNode or FilterNode ? "-" + Child.ToSyntax() : "NON (" + Child.ToSyntax() + ")";

    public override string Describe() => "sauf " + Child.Describe();
}

/// <summary>A word of free text, searched in <see cref="Fields"/>.</summary>
public sealed record TextNode(string Word, TextField Fields = TextField.All) : QueryNode
{
    public override string ToSyntax() => Fields switch
    {
        TextField.Titles or TextField.Name => "titre:" + Quote(Word),
        TextField.Overview => "resume:" + Quote(Word),
        _ => Quote(Word)
    };

    public override string Describe() => Fields switch
    {
        TextField.Titles => "titre contient « " + Word + " »",
        TextField.Overview => "résumé contient « " + Word + " »",
        TextField.Name => "« " + Word + " » dans le titre",
        _ => "« " + Word + " »"
    };
}

/// <summary>Words that must follow each other ("le parrain").</summary>
public sealed record PhraseNode(string Phrase, TextField Fields = TextField.All) : QueryNode
{
    public override string ToSyntax() => (Fields == TextField.Titles ? "titre:" : Fields == TextField.Overview ? "resume:" : string.Empty) + "\"" + Phrase + "\"";

    public override string Describe() => "« " + Phrase + " »" + (Fields == TextField.Titles ? " dans le titre" : Fields == TextField.Overview ? " dans le résumé" : string.Empty);
}

/// <summary>
/// A word of a phrase read as a genre ("comédies"): the genre, or the word in a title ("Action Jackson").
/// Shown and saved as the genre alone.
/// </summary>
public sealed record GenreWordNode(FilterNode Genre, QueryNode Title) : QueryNode
{
    public override string ToSyntax() => Genre.ToSyntax();

    public override string Describe() => Genre.Describe();
}

/// <summary>A filter on a field ("note>7", "genre:comédie|drame", "annee:1990..1999").</summary>
public sealed record FilterNode(FieldInfo Field, Op Op, IReadOnlyList<FilterValue> Values) : QueryNode
{
    public override string ToSyntax()
        => Field.Syntax + OpSyntax(Op) + string.Join('|', Values.Select(v => v.ToSyntax(Field)));

    public override string Describe()
    {
        var values = string.Join(" ou ", Values.Select(v => v.Describe(Field)));
        var not = Op == Op.NotEqual;
        var range = Values.Count == 1 && Values[0].High is not null;
        switch (Field.Id)
        {
            case FieldId.Type:
                return (not ? "sauf les " : string.Empty) + values;
            case FieldId.Watched:
                return Values[0].Bool == false ? "non vus" : "déjà vus";
            case FieldId.Favorite:
                return Values[0].Bool == false ? "hors favoris" : "favoris";
            case FieldId.Available:
                return Values[0].Bool == false ? "disque absent" : "disque branché";
            case FieldId.Sort:
                return "tri par " + values;
            case FieldId.Actor:
                return (not ? "sans " : "avec ") + values;
            case FieldId.Director:
                return (not ? "pas réalisé par " : "réalisé par ") + values;
            case FieldId.Person:
                return (not ? "sans " : "avec ") + values;
            case FieldId.Media:
                return (not ? "pas sur " : "sur ") + values;
            case FieldId.Genre:
            case FieldId.Tag:
            case FieldId.Studio:
                return Field.Label + (not ? " autre que " : " ") + values;
            case FieldId.Title:
            case FieldId.Overview:
                return Field.Label + (not ? " sans « " : " contient « ") + values + " »";
            case FieldId.Year when range && Values[0] is { Number: { } low, High: { } high } && low % 10 == 0 && high == low + 9 && !not:
                return "années " + (low >= 2000 ? FilterValue.Num(low) : FilterValue.Num(low % 100));
            case FieldId.Year when !range:
                return Op switch
                {
                    Op.Greater => "après " + values,
                    Op.GreaterOrEqual => "depuis " + values,
                    Op.Less => "avant " + values,
                    Op.LessOrEqual => "jusqu'à " + values,
                    Op.NotEqual => "pas en " + values,
                    _ => "en " + values
                };
            case FieldId.Added when Values.Count == 1 && Values[0].Days is not null:
                return Op switch
                {
                    Op.Less or Op.LessOrEqual => "ajoutés il y a moins de " + values,
                    Op.Greater or Op.GreaterOrEqual => "ajoutés il y a plus de " + values,
                    _ => "ajoutés il y a " + values
                };
            case FieldId.Added:
                return (Op is Op.Less or Op.LessOrEqual ? "ajoutés avant le " : "ajoutés depuis le ") + values;
        }

        return Field.Label + " " + OpLabel(Op, range, Field.Kind) + " " + values;
    }

    internal static string OpSyntax(Op op) => op switch
    {
        Op.Match => ":",
        Op.Equal => "=",
        Op.NotEqual => "!=",
        Op.Greater => ">",
        Op.GreaterOrEqual => ">=",
        Op.Less => "<",
        _ => "<="
    };

    private static string OpLabel(Op op, bool range, FieldKind kind) => op switch
    {
        _ when range => "de",
        Op.Match => kind == FieldKind.Text ? "≈" : "=",
        Op.Equal => "=",
        Op.NotEqual => "≠",
        Op.Greater => ">",
        Op.GreaterOrEqual => "≥",
        Op.Less => "<",
        _ => "≤"
    };
}

/// <summary>A value of a filter: text, number or range, yes/no, kind of item, age in days, or sort order.</summary>
public sealed record FilterValue
{
    public string? Text { get; init; }

    public double? Number { get; init; }

    /// <summary>Upper bound of a range "1990..1999" (inclusive).</summary>
    public double? High { get; init; }

    public bool? Bool { get; init; }

    public DocKind? Kind { get; init; }

    /// <summary>For "ajout": an age in days ("30j") rather than a date.</summary>
    public double? Days { get; init; }

    public DateTime? Date { get; init; }

    public SortKey? Sort { get; init; }

    public string ToSyntax(FieldInfo field)
    {
        if (Sort is { } s)
        {
            return SortNames.First(x => x.Key == s).Name;
        }

        if (Kind is { } k)
        {
            return KindSyntax(k);
        }

        if (Bool is { } b)
        {
            return b ? "oui" : "non";
        }

        if (Days is { } days)
        {
            return Num(days) + "j";
        }

        if (Date is { } date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (Number is { } n)
        {
            return High is { } h ? Num(n) + ".." + Num(h) : Num(n);
        }

        return QueryNode.Quote(Text ?? string.Empty);
    }

    public string Describe(FieldInfo field)
    {
        if (Sort is { } s)
        {
            return SortNames.First(x => x.Key == s).Label;
        }

        if (Kind is { } k)
        {
            return KindLabel(k);
        }

        if (Bool is { } b)
        {
            return b ? "oui" : "non";
        }

        if (Days is { } days)
        {
            return Num(days) + " jour" + (days > 1 ? "s" : string.Empty);
        }

        if (Date is { } date)
        {
            return date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"));
        }

        if (Number is { } n)
        {
            var unit = field.Id == FieldId.Runtime ? " min" : field.Id == FieldId.Age ? " ans" : string.Empty;
            var fr = field.Id == FieldId.Year ? (Func<double, string>)Num : v => Num(v).Replace('.', ',');
            return High is { } h ? fr(n) + " à " + fr(h) + unit : fr(n) + unit;
        }

        return Text ?? string.Empty;
    }

    internal static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string KindSyntax(DocKind k) => k switch
    {
        DocKind.Movie => "film",
        DocKind.Series => "serie",
        DocKind.Episode => "episode",
        _ => "collection"
    };

    internal static string KindLabel(DocKind k) => k switch
    {
        DocKind.Movie => "films",
        DocKind.Series => "séries",
        DocKind.Episode => "épisodes",
        _ => "collections"
    };

    internal static readonly (SortKey Key, string Name, string Label, string[] Aliases)[] SortNames =
    [
        (SortKey.Tmdb, "note", "note TMDB", ["note", "notes", "tmdb", "rating", "meilleur", "meilleurs"]),
        (SortKey.Imdb, "imdb", "note IMDb", ["imdb"]),
        (SortKey.YearNewest, "annee", "année (récents d'abord)", ["annee", "recent", "recents", "year", "newest", "date"]),
        (SortKey.YearOldest, "ancien", "année (anciens d'abord)", ["ancien", "anciens", "oldest", "vieux"]),
        (SortKey.Added, "ajout", "date d'ajout", ["ajout", "ajoute", "added", "nouveau", "nouveaux"]),
        (SortKey.Title, "titre", "titre", ["titre", "nom", "title", "name", "alpha", "alphabetique"]),
        (SortKey.Runtime, "duree", "durée", ["duree", "runtime", "court", "courts"]),
        (SortKey.Critic, "critique", "note critique", ["critique", "critic", "presse"]),
        (SortKey.Random, "hasard", "hasard", ["hasard", "aleatoire", "random", "surprise"]),
        (SortKey.Relevance, "pertinence", "pertinence", ["pertinence", "relevance", "score"])
    ];
}

/// <summary>A parsed query: the tree, and how it was understood.</summary>
public sealed record ParsedQuery(QueryNode? Root, SortKey Sort, bool NaturalLanguage, bool HasText)
{
    /// <summary>The query in the query language.</summary>
    public string Syntax => Root?.ToSyntax() ?? string.Empty;

    /// <summary>The query in plain French.</summary>
    public string Description => Root?.Describe() ?? string.Empty;
}
