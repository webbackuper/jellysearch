using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>
/// Text folding used everywhere in the search: lower case, no accents ("é" → "e", "œ" → "oe"),
/// punctuation replaced by spaces, so that "L'Été meurtrier" and "l ete meurtrier" are the same text.
/// </summary>
public static class TextFold
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // French
        "a", "au", "aux", "avec", "ce", "ces", "d", "dans", "de", "des", "du", "elle", "en", "et", "eux", "il", "ils",
        "je", "l", "la", "le", "les", "leur", "lui", "ma", "mais", "me", "mes", "moi", "mon", "ne", "nous", "on", "ou",
        "par", "pas", "pour", "qu", "que", "qui", "sa", "se", "ses", "son", "sur", "ta", "te", "tes", "toi", "ton", "tu",
        "un", "une", "vos", "votre", "vous", "y", "s", "c", "j", "m", "n", "t", "est", "sont", "ete", "etre",
        // English
        "an", "and", "are", "as", "at", "be", "by", "for", "from", "in", "is", "it", "of", "on", "or", "that", "the",
        "this", "to", "was", "with"
    };

    /// <summary>Folds <paramref name="text"/>: lower case, no diacritics, words separated by one space.</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var space = true;
        for (var index = 0; index < decomposed.Length; index++)
        {
            var raw = decomposed[index];

            // a decimal number stays one word: "7,5" and "7.5" → "7.5"
            if ((raw == ',' || raw == '.') && sb.Length > 0 && char.IsDigit(sb[^1])
                && index + 1 < decomposed.Length && char.IsDigit(decomposed[index + 1]))
            {
                sb.Append('.');
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(raw);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            var c = char.ToLowerInvariant(raw);
            string? replacement = c switch
            {
                'œ' => "oe",
                'æ' => "ae",
                'ß' => "ss",
                'ø' => "o",
                'đ' => "d",
                'ł' => "l",
                'þ' => "th",
                _ => null
            };
            if (replacement is not null)
            {
                sb.Append(replacement);
                space = false;
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                space = false;
            }
            else if (!space)
            {
                sb.Append(' ');
                space = true;
            }
        }

        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    /// <summary>The folded words of <paramref name="text"/>.</summary>
    public static string[] Tokens(string? text)
    {
        var folded = Fold(text);
        return folded.Length == 0 ? [] : folded.Split(' ');
    }

    /// <summary>Whether <paramref name="token"/> (folded) is a French or English stop word.</summary>
    public static bool IsStopWord(string token) => StopWords.Contains(token);

    /// <summary>
    /// A rough singular of a folded French or English word ("comedies" → "comedie", "animaux" → "animal"),
    /// used to compare genres and type words; never used on titles.
    /// </summary>
    public static string Singular(string token)
    {
        if (token.Length > 4 && token.EndsWith("aux", StringComparison.Ordinal))
        {
            return token[..^3] + "al";
        }

        if (token.Length > 3 && (token[^1] == 's' || token[^1] == 'x') && token[^2] != 's')
        {
            return token[..^1];
        }

        return token;
    }

    /// <summary>A number written as digits ("7", "7,5", "7.5") or as a French or English word ("sept").</summary>
    public static bool TryNumber(string token, out double value)
    {
        if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        var index = Array.IndexOf(NumberWords, token);
        if (index < 0)
        {
            index = Array.IndexOf(NumberWordsEn, token);
        }

        value = index;
        return index >= 0;
    }

    private static readonly string[] NumberWords =
    [
        "zero", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf", "vingt"
    ];

    private static readonly string[] NumberWordsEn =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"
    ];
}
