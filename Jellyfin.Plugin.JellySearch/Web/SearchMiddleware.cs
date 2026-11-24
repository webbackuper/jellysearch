using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.JellySearch.Media;
using Jellyfin.Plugin.JellySearch.Saved;
using Jellyfin.Plugin.JellySearch.Search;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Web;

/// <summary>
/// Answers the searches of every client (web page, Android TV app…): a request "GET /Items?searchTerm=…"
/// for movies, series, episodes or collections is answered with JellySearch's results. Other searches
/// (music, live TV, people, photos) and any failure go to Jellyfin as usual.
/// In the Android TV app, whose rows show 25 items without their number, the first row of a search with
/// filters (or with more results) starts with a card "N résultats" that opens every result in a grid.
/// </summary>
public partial class SearchMiddleware
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}";

    private static readonly Dictionary<string, DocKind> KindNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Movie"] = DocKind.Movie,
        ["Series"] = DocKind.Series,
        ["Episode"] = DocKind.Episode,
        ["BoxSet"] = DocKind.BoxSet
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<SearchMiddleware> _logger;

    public SearchMiddleware(RequestDelegate next, ILogger<SearchMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    [GeneratedRegex("/(?:Users/" + GuidPattern + "/)?Items/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemsRoute();

    public async Task InvokeAsync(HttpContext context, IAuthorizationContext authorizationContext, SearchService search, ResultViews views, IServerApplicationHost appHost)
    {
        var request = context.Request;
        var term = HttpMethods.IsGet(request.Method) && ItemsRoute().IsMatch(request.Path.Value ?? string.Empty)
            ? request.Query["searchTerm"].ToString()
            : string.Empty;
        // until the index is built (a few seconds after startup), Jellyfin answers as usual
        if (string.IsNullOrWhiteSpace(term) || !Plugin.CurrentConfiguration.EnableSearch || search.Index.Docs.Count == 0
            || KindsAsked(request.Query) is not { Ours.Count: > 0 } asked)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var auth = await authorizationContext.GetAuthorizationInfo(context).ConfigureAwait(false);
        if (!auth.IsAuthenticated || auth.User is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        List<Guid> ids;
        SearchOutcome outcome;
        var start = int.TryParse(request.Query["startIndex"], out var s) ? Math.Max(0, s) : 0;
        var limit = int.TryParse(request.Query["limit"], out var l) && l > 0 ? Math.Min(l, 800) : 800;
        JsonNode? card = null;
        try
        {
            outcome = search.Search(auth.User, term);
            var parent = Guid.TryParse(request.Query["parentId"], out var p) && p != Guid.Empty ? p : (Guid?)null;
            ids = search.Visible(auth.User, outcome, asked.Ours, parent);
            // a row of the TV app (25 items), not a client asking for one item or a few
            if (start == 0 && limit >= 10 && parent is null && asked.Others.Count == 0 && Plugin.CurrentConfiguration.TvResultsCard
                && ClientClassifier.Classify(auth.Client) == ClientKind.AndroidTv
                && ResultsCard(auth.User, term, outcome, asked.Ours, limit, search, views, appHost.SystemId) is { } dto)
            {
                card = JsonNode.Parse(JsonSerializer.Serialize(dto, JsonDefaults.Options));
            }

            // the web page asks for every kind at once: the others (music, photos, books…) are Jellyfin's own search
            if (asked.Others.Count > 0)
            {
                ids.AddRange(search.Native(auth.User, term, asked.Others, parent).Where(id => !ids.Contains(id)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "JellySearch: recherche « {Term} » laissée à Jellyfin", term);
            await _next(context).ConfigureAwait(false);
            return;
        }

        var page = ids.Skip(start).Take(limit).ToList();
        await IdsForwarder.ForwardAsync(context, _next, page, ids.Count, start, outcome.Query.Syntax, card).ConfigureAwait(false);
    }

    /// <summary>
    /// The card "N résultats" of a TV search, for the first row that has results (movies, else series), when
    /// the search filters or has more results than the row shows; null otherwise.
    /// </summary>
    private static BaseItemDto? ResultsCard(User user, string term, SearchOutcome outcome, HashSet<DocKind> asked, int rowLimit, SearchService search, ResultViews views, string serverId)
    {
        if (asked.Count != 1)
        {
            return null;
        }

        var docs = search.VisibleDocs(user, outcome, null);
        var movies = docs.Count(d => d.Kind == DocKind.Movie);
        var series = docs.Count(d => d.Kind == DocKind.Series);
        var row = movies > 0 ? DocKind.Movie : series > 0 ? DocKind.Series : (DocKind?)null;
        if (row is not { } kind || !asked.Contains(kind))
        {
            return null;
        }

        var filtered = outcome.Query.HasFilters || outcome.Query.Sort != SortKey.Relevance;
        if (!filtered && (kind == DocKind.Movie ? movies : series) <= rowLimit)
        {
            return null;
        }

        var view = views.Put(user.Id, term, outcome.Query.Description, docs.Count, ParsedQuery.SortLabel(outcome.Sort));
        return CardDto(view, serverId);
    }

    /// <summary>The card of a <see cref="ResultView"/>: a library of type "collections", opened as a grid by the TV app.</summary>
    internal static BaseItemDto CardDto(ResultView view, string serverId) => new()
    {
        Name = view.Count == 1 ? "1 résultat" : view.Count.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")).Replace('\u202f', ' ').Replace('\u00a0', ' ') + " résultats",
        ServerId = serverId,
        Id = view.Id,
        Type = BaseItemKind.CollectionFolder,
        CollectionType = CollectionType.boxsets,
        IsFolder = true,
        Overview = view.Description,
        DisplayPreferencesId = ResultViews.DisplayPreferencesId,
        PrimaryImageAspectRatio = 16d / 9d,
        ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = CardTag(view) },
        BackdropImageTags = [],
        LocationType = LocationType.Virtual
    };

    // changes with what the image shows: the TV app downloads the new one
    internal static string CardTag(ResultView view)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("16:9|" + view.Count + "|" + view.Description + "|" + view.SortLabel + "|" + typeof(SearchMiddleware).Assembly.GetName().Version))).ToLowerInvariant();

    /// <summary>What a request asks for: the kinds JellySearch answers for, and the other kinds.</summary>
    internal sealed record Asked(HashSet<DocKind> Ours, List<BaseItemKind> Others);

    /// <summary>The kinds asked, or null when the request is not for JellySearch (videos by media type…).</summary>
    internal static Asked? KindsAsked(IQueryCollection query)
    {
        if (query["mediaTypes"].Count > 0 && !string.IsNullOrEmpty(query["mediaTypes"].ToString()))
        {
            return null;
        }

        var include = IdsForwarder.List(query, "includeItemTypes");
        var exclude = IdsForwarder.List(query, "excludeItemTypes");
        var kinds = new HashSet<DocKind>();
        var others = new List<BaseItemKind>();
        if (include.Count == 0)
        {
            kinds.UnionWith(KindNames.Values);
        }
        else
        {
            foreach (var name in include)
            {
                if (KindNames.TryGetValue(name, out var kind))
                {
                    kinds.Add(kind);
                }
                else if (Enum.TryParse<BaseItemKind>(name, true, out var other))
                {
                    others.Add(other);
                }
            }
        }

        foreach (var name in exclude)
        {
            if (KindNames.TryGetValue(name, out var kind))
            {
                kinds.Remove(kind);
            }
        }

        return new Asked(kinds, others);
    }
}
