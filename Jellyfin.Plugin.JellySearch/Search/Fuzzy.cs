using System;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>Typo tolerance: edit distance with transpositions ("hary" → "harry", "ailen" → "alien").</summary>
public static class Fuzzy
{
    /// <summary>The number of typos allowed in a folded word of <paramref name="length"/> letters.</summary>
    public static int Allowed(int length) => length switch
    {
        < 4 => 0,
        < 8 => 1,
        _ => 2
    };

    /// <summary>
    /// The edit distance between <paramref name="a"/> and <paramref name="b"/> (insertion, deletion,
    /// substitution, swap of two neighbours), or <paramref name="max"/> + 1 when it is larger than <paramref name="max"/>.
    /// </summary>
    public static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max)
        {
            return max + 1;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return Math.Max(a.Length, b.Length);
        }

        var previous2 = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    value = Math.Min(value, previous2[j - 2] + 1);
                }

                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }

            if (rowMin > max)
            {
                return max + 1;
            }

            (previous2, previous, current) = (previous, current, previous2);
        }

        return Math.Min(previous[b.Length], max + 1);
    }

    /// <summary>Whether two folded words are the same word, give or take the typos allowed for their length.</summary>
    public static bool Similar(string query, string word)
    {
        if (string.Equals(query, word, StringComparison.Ordinal))
        {
            return true;
        }

        var allowed = Allowed(Math.Min(query.Length, word.Length));
        return allowed > 0 && Distance(query, word, allowed) <= allowed;
    }
}
