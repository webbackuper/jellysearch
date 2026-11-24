using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.JellySearch.Saved;

/// <summary>A search of the TV app shown as a card "N résultats" that opens every result in a grid.</summary>
public sealed record ResultView(Guid Id, Guid UserId, string Query, string Description, int Count, string SortLabel, DateTime Created);

/// <summary>
/// The searches of the TV app opened as a grid, kept in memory a day (the card of a search names one).
/// The id depends only on the user and the text: the same search gives the same card.
/// </summary>
public sealed class ResultViews
{
    /// <summary>The display preferences shared by every result grid (the TV app keeps there the order chosen).</summary>
    public const string DisplayPreferencesId = "jellysearch-results";

    private static readonly TimeSpan Life = TimeSpan.FromDays(1);
    private readonly ConcurrentDictionary<Guid, ResultView> _views = new();

    /// <summary>The view of <paramref name="query"/> for <paramref name="userId"/>, created or updated.</summary>
    public ResultView Put(Guid userId, string query, string description, int count, string sortLabel)
    {
        var id = new Guid(MD5.HashData(Encoding.UTF8.GetBytes("jellysearch-results|" + userId.ToString("N") + "|" + query.Trim())));
        var view = new ResultView(id, userId, query.Trim(), description, count, sortLabel, DateTime.UtcNow);
        _views[id] = view;
        if (_views.Count > 300)
        {
            var now = DateTime.UtcNow;
            foreach (var old in _views.Values.Where(v => now - v.Created > Life).Select(v => v.Id).ToList())
            {
                _views.TryRemove(old, out _);
            }
        }

        return view;
    }

    /// <summary>The view <paramref name="id"/>, if known.</summary>
    public ResultView? Get(Guid id) => _views.TryGetValue(id, out var v) ? v : null;
}
