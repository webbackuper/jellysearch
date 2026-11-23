using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>
/// Parses the query language: free words, "quoted phrases", filters "champ opérateur valeur"
/// (note>7, genre:comédie|drame, annee:1990..1999, duree&lt;1h30, vu:non), ET / OU / NON (or AND / OR /
/// NOT, &amp;&amp; / || / -mot) and parentheses. Runs of plain words are read by <see cref="NaturalQuery"/>,
/// which understands phrases such as "comédies des années 90 avec Jim Carrey".
/// </summary>
public static partial class QueryParser
{
    private enum LexKind
    {
        Word,
        Phrase,
        Filter,
        LParen,
        RParen,
        And,
        Or,
        Not
    }

    private sealed record Lexeme(LexKind Kind, string Text, FilterNode? Filter = null);

    [GeneratedRegex("^(?<field>[\\p{L}][\\p{L}\\-']*)(?<op>>=|<=|!=|=|:|>|<|~)(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex FilterChunk();

    [GeneratedRegex("^(>=|<=|!=|=|:|>|<|~)(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorChunk();

    /// <summary>Parses <paramref name="input"/>; <paramref name="vocabulary"/> lets the phrases recognise genres, people and MediaIDs.</summary>
    public static ParsedQuery Parse(string? input, SearchIndex vocabulary, bool naturalLanguage = true)
    {
        var lexemes = Lex(input ?? string.Empty);
        var state = new ParserState(lexemes, vocabulary, naturalLanguage);
        var root = state.ParseOr();

        // "tri:" filters are not conditions: they set the order
        var sort = SortKey.Relevance;
        root = ExtractSort(root, ref sort);
        root = DropStopWords(root);
        root = Canonical(root, vocabulary);
        var hasText = HasText(root);
        if (sort == SortKey.Relevance && state.SuggestedSort is { } suggested)
        {
            sort = suggested;
        }

        return new ParsedQuery(root, sort, state.NaturalLanguageUsed, hasText);
    }

    /// <summary>A parse without the phrases: every plain word is free text (the fallback when the phrases find nothing).</summary>
    public static ParsedQuery ParseLiteral(string? input, SearchIndex vocabulary) => Parse(input, vocabulary, false);

    private static List<Lexeme> Lex(string input)
    {
        var chunks = new List<(string Text, bool Quoted)>();
        var i = 0;
        while (i < input.Length)
        {
            var c = input[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '(' or ')')
            {
                chunks.Add((c.ToString(), false));
                i++;
                continue;
            }

            if (c is '"' or '«' or '“')
            {
                var end = input.IndexOfAny(['"', '»', '”'], i + 1);
                var text = end < 0 ? input[(i + 1)..] : input[(i + 1)..end];
                chunks.Add((text.Trim(), true));
                i = end < 0 ? input.Length : end + 1;
                continue;
            }

            var sb = new StringBuilder();
            while (i < input.Length && !char.IsWhiteSpace(input[i]) && input[i] is not '(' and not ')')
            {
                if (input[i] is '"' or '«' or '“' && sb.Length > 0 && sb[^1] is ':' or '=' or '>' or '<' or '~')
                {
                    // acteur:"Bruce Willis"
                    var end = input.IndexOfAny(['"', '»', '”'], i + 1);
                    var quoted = end < 0 ? input[(i + 1)..] : input[(i + 1)..end];
                    sb.Append('\u0001').Append(quoted.Trim());
                    i = end < 0 ? input.Length : end + 1;
                    break;
                }

                sb.Append(input[i]);
                i++;
            }

            chunks.Add((sb.ToString(), false));
        }

        var result = new List<Lexeme>();
        for (var k = 0; k < chunks.Count; k++)
        {
            var (text, quoted) = chunks[k];
            if (quoted)
            {
                if (text.Length > 0)
                {
                    result.Add(new Lexeme(LexKind.Phrase, text));
                }

                continue;
            }

            switch (text)
            {
                case "(":
                    result.Add(new Lexeme(LexKind.LParen, text));
                    continue;
                case ")":
                    result.Add(new Lexeme(LexKind.RParen, text));
                    continue;
                case "ET" or "AND" or "&&" or "&":
                    result.Add(new Lexeme(LexKind.And, text));
                    continue;
                case "OU" or "OR" or "||" or "|":
                    result.Add(new Lexeme(LexKind.Or, text));
                    continue;
                case "NON" or "NOT" or "!" or "SAUF" or "-":
                    result.Add(new Lexeme(LexKind.Not, text));
                    continue;
            }

            // -mot, !genre:horreur
            if (text.Length > 1 && text[0] is '-' or '!' && !char.IsDigit(text[1]))
            {
                result.Add(new Lexeme(LexKind.Not, text[..1]));
                text = text[1..];
            }

            // champ:valeur, champ>valeur, champ:"valeur"
            var m = FilterChunk().Match(text);
            if (m.Success && Fields.Find(m.Groups["field"].Value) is { } field)
            {
                var value = m.Groups["value"].Value.TrimStart('\u0001');
                if (value.Length == 0 && k + 1 < chunks.Count && chunks[k + 1].Text is not "(" and not ")")
                {
                    value = chunks[++k].Text;
                }

                if (BuildFilter(field, ParseOp(m.Groups["op"].Value), value) is { } filter)
                {
                    result.Add(new Lexeme(LexKind.Filter, text, filter));
                    continue;
                }
            }

            // "note > 7", "note >7"
            if (Fields.Find(text) is { } spaced && k + 1 < chunks.Count && !chunks[k + 1].Quoted && OperatorChunk().Match(chunks[k + 1].Text) is { Success: true } om)
            {
                var opText = chunks[k + 1].Text[..(chunks[k + 1].Text.Length - om.Groups["value"].Value.Length)];
                var value = om.Groups["value"].Value;
                var used = 1;
                if (value.Length == 0 && k + 2 < chunks.Count)
                {
                    value = chunks[k + 2].Text;
                    used = 2;
                }

                if (BuildFilter(spaced, ParseOp(opText), value) is { } filter)
                {
                    result.Add(new Lexeme(LexKind.Filter, text + opText + value, filter));
                    k += used;
                    continue;
                }
            }

            result.Add(new Lexeme(LexKind.Word, text.Replace("\u0001", " ", StringComparison.Ordinal)));
        }

        return result;
    }

    private static Op ParseOp(string op) => op switch
    {
        "=" => Op.Equal,
        "!=" => Op.NotEqual,
        ">" => Op.Greater,
        ">=" => Op.GreaterOrEqual,
        "<" => Op.Less,
        "<=" => Op.LessOrEqual,
        _ => Op.Match
    };

    /// <summary>A filter from its parts, or null when the value does not fit the field.</summary>
    public static FilterNode? BuildFilter(FieldInfo field, Op op, string value)
    {
        value = value.Trim();
        if (value.Length == 0 && field.Kind != FieldKind.Boolean)
        {
            return null;
        }

        // several values: genre:comedie|drame, type:film,serie (a comma is a decimal point in numbers)
        var parts = field.Kind == FieldKind.Number
            ? value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : value.Split(['|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            parts = [string.Empty];
        }

        var values = new List<FilterValue>();
        foreach (var part in parts)
        {
            var v = ParseValue(field, op, part);
            if (v is null)
            {
                return null;
            }

            values.Add(v);
        }

        // "vu!=oui" is "vu:non"
        if (field.Kind == FieldKind.Boolean && op == Op.NotEqual)
        {
            values = values.Select(v => v with { Bool = !(v.Bool ?? true) }).ToList();
            op = Op.Match;
        }

        return new FilterNode(field, op, values);
    }

    private static FilterValue? ParseValue(FieldInfo field, Op op, string raw)
    {
        var folded = TextFold.Fold(raw);
        switch (field.Kind)
        {
            case FieldKind.Text:
                return folded.Length == 0 ? null : new FilterValue { Text = raw.Trim() };

            case FieldKind.Boolean:
                return folded switch
                {
                    "" or "oui" or "yes" or "true" or "vrai" or "1" or "o" or "y" => new FilterValue { Bool = true },
                    "non" or "no" or "false" or "faux" or "0" or "n" => new FilterValue { Bool = false },
                    _ => null
                };

            case FieldKind.Kind:
                return ParseKind(folded) is { } kind ? new FilterValue { Kind = kind } : null;

            case FieldKind.Sort:
                var sort = FilterValue.SortNames.FirstOrDefault(s => s.Aliases.Contains(folded));
                return sort.Name is null ? null : new FilterValue { Sort = sort.Key };

            case FieldKind.Added:
                return ParseAdded(raw);

            default:
                return ParseNumberValue(field.Id, op, raw);
        }
    }

    internal static DocKind? ParseKind(string folded) => TextFold.Singular(folded) switch
    {
        "film" or "movie" or "long metrage" => DocKind.Movie,
        "serie" or "show" or "tv" or "feuilleton" or "series" or "tvshow" => DocKind.Series,
        "episode" => DocKind.Episode,
        "collection" or "saga" or "boxset" or "coffret" => DocKind.BoxSet,
        _ => null
    };

    private static FilterValue? ParseAdded(string raw)
    {
        var text = raw.Trim().ToLowerInvariant();
        var m = Regex.Match(text, "^(\\d+(?:[.,]\\d+)?)\\s*(j|jour|jours|d|day|days|s|sem|semaine|semaines|w|week|weeks|m|mois|month|months|a|an|ans|annee|annees|y|year|years)?$", RegexOptions.CultureInvariant);
        if (m.Success)
        {
            var n = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var unit = m.Groups[2].Value;
            var days = unit switch
            {
                "s" or "sem" or "semaine" or "semaines" or "w" or "week" or "weeks" => n * 7,
                "m" or "mois" or "month" or "months" => n * 30.4,
                "a" or "an" or "ans" or "annee" or "annees" or "y" or "year" or "years" => n * 365,
                _ => n
            };

            // a bare number over 1900 is a year
            if (unit.Length == 0 && n >= 1900)
            {
                return new FilterValue { Date = new DateTime((int)n, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            }

            return new FilterValue { Days = Math.Round(days, 1) };
        }

        string[] formats = ["yyyy-MM-dd", "yyyy-MM", "dd/MM/yyyy", "d/M/yyyy", "yyyy"];
        return DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            ? new FilterValue { Date = date }
            : null;
    }

    private static FilterValue? ParseNumberValue(FieldId id, Op op, string raw)
    {
        var text = raw.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);

        // ranges: 1990..1999, 1990-1999, 7..8
        var range = Regex.Match(text, "^(?<a>.+?)(?:\\.\\.|à)(?<b>.+)$", RegexOptions.CultureInvariant);
        if (!range.Success)
        {
            range = Regex.Match(text, "^(?<a>[\\dh.,]+?)-(?<b>[\\dh.,]+)$", RegexOptions.CultureInvariant);
        }

        if (range.Success && id != FieldId.Age)
        {
            var low = ParseNumber(id, Op.GreaterOrEqual, range.Groups["a"].Value, out _);
            var high = ParseNumber(id, Op.LessOrEqual, range.Groups["b"].Value, out _);
            if (low is not null && high is not null)
            {
                return new FilterValue { Number = Math.Min(low.Value, high.Value), High = Math.Max(low.Value, high.Value) };
            }
        }

        var number = ParseNumber(id, op, text, out var span);
        if (number is null)
        {
            return null;
        }

        // "annee:80" or "annee:80s": the decade
        return span is { } s && op is Op.Match or Op.Equal ? new FilterValue { Number = number, High = number + s } : new FilterValue { Number = number };
    }

    /// <summary>A number of the field; <paramref name="span"/> is set for a decade ("80s": 1980, span 9).</summary>
    private static double? ParseNumber(FieldId id, Op op, string text, out double? span)
    {
        span = null;
        switch (id)
        {
            case FieldId.Year:
            {
                var decade = Regex.Match(text, "^(\\d{2}|\\d{4})s$|^(\\d{2})$", RegexOptions.CultureInvariant);
                if (decade.Success)
                {
                    var digits = decade.Groups[1].Success ? decade.Groups[1].Value : decade.Groups[2].Value;
                    var year = int.Parse(digits, CultureInfo.InvariantCulture);
                    if (digits.Length == 2)
                    {
                        year += year <= 30 ? 2000 : 1900;
                    }

                    if (decade.Groups[1].Success || op is Op.Match or Op.Equal)
                    {
                        span = 9;
                        return year - (year % 10);
                    }

                    return year;
                }

                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1800 and <= 2200 ? y : null;
            }

            case FieldId.Runtime:
                return ParseMinutes(text);

            case FieldId.ImdbVotes:
            {
                var m = Regex.Match(text, "^(\\d+(?:[.,]\\d+)?)(k|m)?$", RegexOptions.CultureInvariant);
                if (!m.Success)
                {
                    return null;
                }

                var n = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                return m.Groups[2].Value switch { "k" => n * 1000, "m" => n * 1_000_000, _ => n };
            }

            case FieldId.Age:
            {
                if (text is "tp" or "tous" or "toutpublic" or "u" or "g" or "all")
                {
                    return 0;
                }

                var digits = new string(text.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
            }

            default:
            {
                // ratings: 7, 7.5, 7,5, 7/10, 80%
                var cleaned = text.TrimEnd('%');
                var slash = cleaned.IndexOf('/', StringComparison.Ordinal);
                if (slash > 0)
                {
                    cleaned = cleaned[..slash];
                }

                return double.TryParse(cleaned.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null;
            }
        }
    }

    /// <summary>Minutes from "90", "90min", "1h30", "2h", "1h".</summary>
    internal static double? ParseMinutes(string text)
    {
        var m = Regex.Match(text, "^(?<h>\\d+)h(?<m>\\d{1,2})?(?:min|mn|m)?$|^(?<min>\\d+(?:[.,]\\d+)?)(?:min|mn|m|minutes?)?$", RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return null;
        }

        if (m.Groups["h"].Success)
        {
            return (int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture) * 60) + (m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0);
        }

        return double.Parse(m.Groups["min"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    private static QueryNode? ExtractSort(QueryNode? node, ref SortKey sort)
    {
        switch (node)
        {
            case FilterNode { Field.Kind: FieldKind.Sort } f:
                sort = f.Values.FirstOrDefault()?.Sort ?? SortKey.Relevance;
                return null;
            case AndNode and:
            {
                var children = new List<QueryNode>();
                foreach (var c in and.Children)
                {
                    if (ExtractSort(c, ref sort) is { } kept)
                    {
                        children.Add(kept);
                    }
                }

                return children.Count switch { 0 => null, 1 => children[0], _ => new AndNode(children) };
            }

            default:
                return node;
        }
    }

    // "le", "des"… are dropped when the query has other words
    private static QueryNode? DropStopWords(QueryNode? root)
    {
        var words = new List<TextNode>();
        Collect(root, words);
        if (!words.Any(w => !TextFold.IsStopWord(w.Word)))
        {
            return root;
        }

        return Prune(root);

        static void Collect(QueryNode? n, List<TextNode> into)
        {
            switch (n)
            {
                case TextNode t:
                    into.Add(t);
                    break;
                case AndNode a:
                    a.Children.ToList().ForEach(c => Collect(c, into));
                    break;
                case OrNode o:
                    o.Children.ToList().ForEach(c => Collect(c, into));
                    break;
                case NotNode x:
                    Collect(x.Child, into);
                    break;
            }
        }

        static QueryNode? Prune(QueryNode? n)
        {
            switch (n)
            {
                case TextNode t when TextFold.IsStopWord(t.Word):
                    return null;
                case AndNode a:
                {
                    var children = a.Children.Select(Prune).Where(c => c is not null).Cast<QueryNode>().ToList();
                    return children.Count switch { 0 => null, 1 => children[0], _ => new AndNode(children) };
                }

                case OrNode o:
                {
                    var children = o.Children.Select(Prune).Where(c => c is not null).Cast<QueryNode>().ToList();
                    return children.Count switch { 0 => null, 1 => children[0], _ => new OrNode(children) };
                }

                case NotNode x:
                    return Prune(x.Child) is { } c ? new NotNode(c) : null;
                default:
                    return n;
            }
        }
    }

    // "genre:comedie" is shown (and saved) as the library writes it: "genre:Comédie"
    private static QueryNode? Canonical(QueryNode? node, SearchIndex index)
    {
        switch (node)
        {
            case FilterNode f when f.Field.Kind == FieldKind.Text && f.Op != Op.Equal:
            {
                var values = f.Values.Select(v => v.Text is { } text ? v with { Text = DisplayOf(f.Field.Id, text, index) } : v).ToList();
                return f with { Values = values };
            }

            case AndNode a:
                return new AndNode(a.Children.Select(c => Canonical(c, index)).Where(c => c is not null).Cast<QueryNode>().ToList());
            case OrNode o:
                return new OrNode(o.Children.Select(c => Canonical(c, index)).Where(c => c is not null).Cast<QueryNode>().ToList());
            case NotNode n:
                return Canonical(n.Child, index) is { } child ? new NotNode(child) : null;
            default:
                return node;
        }
    }

    private static string DisplayOf(FieldId field, string text, SearchIndex index)
    {
        IReadOnlyDictionary<string, string>? vocabulary = field switch
        {
            FieldId.Genre => index.Genres,
            FieldId.Tag => index.Tags,
            FieldId.Studio => index.Studios,
            FieldId.Media => index.MediaIds,
            _ => null
        };
        if (vocabulary is not null)
        {
            var found = SearchIndex.MatchValues(vocabulary, text, false);
            return found.Count == 1 ? vocabulary[found.First()] : text;
        }

        if (field is FieldId.Actor or FieldId.Director or FieldId.Person)
        {
            var roles = field == FieldId.Actor ? TextField.Actor : field == FieldId.Director ? TextField.Director : TextField.People;
            var people = index.MatchPeople(text, roles);
            return people.Count == 1 && index.PeopleNames.TryGetValue(people.First(), out var p) ? p.Display : text;
        }

        return text;
    }

    private static bool HasText(QueryNode? node) => node switch
    {
        TextNode or PhraseNode => true,
        GenreWordNode => false,
        AndNode a => a.Children.Any(HasText),
        OrNode o => o.Children.All(HasText),
        _ => false
    };

    private sealed class ParserState
    {
        private readonly List<Lexeme> _lexemes;
        private readonly SearchIndex _vocabulary;
        private readonly bool _naturalLanguage;
        private int _pos;

        public ParserState(List<Lexeme> lexemes, SearchIndex vocabulary, bool naturalLanguage)
        {
            _lexemes = lexemes;
            _vocabulary = vocabulary;
            _naturalLanguage = naturalLanguage;
        }

        public bool NaturalLanguageUsed { get; private set; }

        public SortKey? SuggestedSort { get; private set; }

        private Lexeme? Peek => _pos < _lexemes.Count ? _lexemes[_pos] : null;

        public QueryNode? ParseOr()
        {
            var children = new List<QueryNode>();
            var first = ParseAnd();
            if (first is not null)
            {
                children.Add(first);
            }

            while (Peek is { Kind: LexKind.Or })
            {
                _pos++;
                if (ParseAnd() is { } next)
                {
                    children.Add(next);
                }
            }

            return children.Count switch { 0 => null, 1 => children[0], _ => new OrNode(children) };
        }

        private QueryNode? ParseAnd()
        {
            var children = new List<QueryNode>();
            while (Peek is { } lex && lex.Kind != LexKind.Or && lex.Kind != LexKind.RParen)
            {
                if (lex.Kind == LexKind.And)
                {
                    _pos++;
                    continue;
                }

                if (lex.Kind == LexKind.Word)
                {
                    // a run of plain words: read as a whole (phrases)
                    var words = new List<string>();
                    while (Peek is { Kind: LexKind.Word } w)
                    {
                        words.Add(w.Text);
                        _pos++;
                    }

                    children.AddRange(ReadWords(words));
                    continue;
                }

                if (ParseUnary() is { } node)
                {
                    children.Add(node);
                }
            }

            return children.Count switch { 0 => null, 1 => children[0], _ => new AndNode(children) };
        }

        private QueryNode? ParseUnary()
        {
            var lex = Peek;
            if (lex is null)
            {
                return null;
            }

            if (lex.Kind == LexKind.Not)
            {
                _pos++;
                return ParseUnary() is { } inner ? new NotNode(inner) : null;
            }

            _pos++;
            switch (lex.Kind)
            {
                case LexKind.LParen:
                {
                    var inner = ParseOr();
                    if (Peek is { Kind: LexKind.RParen })
                    {
                        _pos++;
                    }

                    return inner;
                }

                case LexKind.Filter:
                    return lex.Filter;
                case LexKind.Phrase:
                {
                    var folded = TextFold.Fold(lex.Text);
                    return folded.Length == 0 ? null : folded.Contains(' ', StringComparison.Ordinal) ? new PhraseNode(folded) : new TextNode(folded);
                }

                case LexKind.Word:
                {
                    var tokens = TextFold.Tokens(lex.Text);
                    return tokens.Length switch
                    {
                        0 => null,
                        1 => new TextNode(tokens[0]),
                        _ => new AndNode(tokens.Select(t => (QueryNode)new TextNode(t)).ToList())
                    };
                }

                default:
                    return null;
            }
        }

        private IEnumerable<QueryNode> ReadWords(List<string> words)
        {
            var tokens = words.SelectMany(TextFold.Tokens).ToList();
            if (_naturalLanguage)
            {
                var reading = NaturalQuery.Read(tokens, _vocabulary);
                if (reading.Understood)
                {
                    NaturalLanguageUsed = true;
                    SuggestedSort ??= reading.Sort;
                    return reading.Nodes;
                }
            }

            return tokens.Select(t => (QueryNode)new TextNode(t));
        }
    }
}
