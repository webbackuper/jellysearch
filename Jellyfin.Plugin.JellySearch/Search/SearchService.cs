using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.JellySearch.Media;
using Jellyfin.Plugin.JellySearch.Ratings;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.JellySearch.Search;

/// <summary>
/// Searches for a user: the index, the user's rights (what they can see, what they watched, their
/// favourites, read from Jellyfin) and a short cache, because the TV app sends one request per row
/// (movies, series, episodes…) for the same text.
/// </summary>
public sealed class SearchService
{
    private static readonly TimeSpan ResultTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SetTime = TimeSpan.FromSeconds(10);
    private static readonly BaseItemKind[] Kinds = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Episode, BaseItemKind.BoxSet];

    private readonly LibraryIndex _index;
    private readonly ILibraryManager _libraryManager;
    private readonly MediaPresence _presence;
    private readonly ImdbRatings _ratings;
    private readonly ConcurrentDictionary<string, (DateTime At, Lazy<SearchOutcome> Outcome)> _results = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (DateTime At, Lazy<HashSet<Guid>> Ids)> _sets = new(StringComparer.Ordinal);

    public SearchService(LibraryIndex index, ILibraryManager libraryManager, MediaPresence presence, ImdbRatings ratings)
    {
        _index = index;
        _libraryManager = libraryManager;
        _presence = presence;
        _ratings = ratings;
        _index.Rebuilt += (_, _) =>
        {
            _results.Clear();
            _sets.Clear();
        };
    }

    /// <summary>The index in use.</summary>
    public SearchIndex Index => _index.Current;

    /// <summary>The ratings in use.</summary>
    public ImdbRatings Ratings => _ratings;

    /// <summary>Searches <paramref name="text"/> for <paramref name="user"/> (cached a few seconds).</summary>
    public SearchOutcome Search(User user, string text)
    {
        var key = user.Id.ToString("N") + "|" + _index.Version + "|" + text.Trim();
        var now = DateTime.UtcNow;
        if (_results.Count > 500)
        {
            foreach (var old in _results.Where(r => now - r.Value.At > ResultTime).Select(r => r.Key).ToList())
            {
                _results.TryRemove(old, out _);
            }
        }

        if (!_results.TryGetValue(key, out var entry) || now - entry.At > ResultTime)
        {
            entry = (now, new Lazy<SearchOutcome>(() => Run(user, text)));
            _results[key] = entry;
        }

        return entry.Outcome.Value;
    }

    /// <summary>How <paramref name="text"/> is understood (no user needed).</summary>
    public ParsedQuery Explain(string text)
        => QueryParser.Parse(text, _index.Current, Plugin.CurrentConfiguration.NaturalLanguage);

    /// <summary>The items of the indexed kinds that <paramref name="user"/> may see (rights and parental control).</summary>
    public HashSet<Guid> Visible(User user)
        => Set(user, "visible", () => new InternalItemsQuery(user) { IncludeItemTypes = Kinds, Recursive = true, IsVirtualItem = false });

    /// <summary>The items below <paramref name="parentId"/> that <paramref name="user"/> may see.</summary>
    public HashSet<Guid> Below(User user, Guid parentId)
    {
        var key = user.Id.ToString("N") + "|" + _index.Version + "|parent:" + parentId.ToString("N");
        var now = DateTime.UtcNow;
        if (!_sets.TryGetValue(key, out var entry) || now - entry.At > SetTime)
        {
            // through the folder itself: a library (collection folder) is not the parent of its items in the database
            entry = (now, new Lazy<HashSet<Guid>>(() => _libraryManager.GetItemById(parentId) is Folder folder
                ? folder.GetItemList(new InternalItemsQuery(user) { IncludeItemTypes = Kinds, Recursive = true, IsVirtualItem = false, DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false) }).Select(i => i.Id).ToHashSet()
                : []));
            _sets[key] = entry;
        }

        return entry.Ids.Value;
    }

    /// <summary>The documents of <paramref name="outcome"/> the user may see, of <paramref name="kinds"/>, below <paramref name="parentId"/>.</summary>
    public List<Guid> Visible(User user, SearchOutcome outcome, IReadOnlySet<DocKind> kinds, Guid? parentId)
    {
        var index = _index.Current;
        var visible = Visible(user);
        var below = parentId is { } p ? Below(user, p) : null;
        var ids = new List<Guid>(outcome.Docs.Count);
        foreach (var docIndex in outcome.Docs)
        {
            if (docIndex >= index.Docs.Count)
            {
                continue;
            }

            var doc = index.Docs[docIndex];
            if (kinds.Contains(doc.Kind) && visible.Contains(doc.Id) && (below is null || below.Contains(doc.Id)))
            {
                ids.Add(doc.Id);
            }
        }

        return ids;
    }

    /// <summary>Jellyfin's own search, for the kinds JellySearch does not answer for (music, photos, books…).</summary>
    public IEnumerable<Guid> Native(User user, string text, IReadOnlyList<BaseItemKind> kinds, Guid? parentId)
        => _libraryManager.GetItemIds(new InternalItemsQuery(user)
        {
            SearchTerm = text,
            IncludeItemTypes = kinds.ToArray(),
            Recursive = true,
            IsVirtualItem = false,
            AncestorIds = parentId is { } p ? [p] : [],
            Limit = 300
        });

    private SearchOutcome Run(User user, string text)
    {
        var config = Plugin.CurrentConfiguration;
        var fields = TextField.Titles;
        if (config.SearchOverview)
        {
            fields |= TextField.Overview;
        }

        if (config.SearchPeople)
        {
            fields |= TextField.People;
        }

        if (config.SearchTags)
        {
            fields |= TextField.Genre | TextField.Tag | TextField.Studio;
        }

        var basePath = config.BasePath;
        var context = new SearchContext
        {
            Played = () => Set(user, "played", () => new InternalItemsQuery(user) { IncludeItemTypes = Kinds, Recursive = true, IsPlayed = true }),
            Favorites = () => Set(user, "favorites", () => new InternalItemsQuery(user) { IncludeItemTypes = Kinds, Recursive = true, IsFavorite = true }),
            IsMediaPresent = mediaId => _presence.IsPresent(basePath, mediaId),
            Imdb = id => _ratings.Get(id),
            RandomSeed = HashCode.Combine(user.Id, text, DateTime.UtcNow.DayOfYear),
            Typos = config.Typos,
            TextFields = fields
        };
        return SearchEngine.Search(_index.Current, text, context, config.NaturalLanguage);
    }

    private HashSet<Guid> Set(User user, string name, Func<InternalItemsQuery> query)
    {
        var key = user.Id.ToString("N") + "|" + _index.Version + "|" + name;
        var now = DateTime.UtcNow;
        if (!_sets.TryGetValue(key, out var entry) || now - entry.At > SetTime)
        {
            entry = (now, new Lazy<HashSet<Guid>>(() => _libraryManager.GetItemIds(query()).ToHashSet()));
            _sets[key] = entry;
        }

        return entry.Ids.Value;
    }
}
