using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellySearch.Search;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Web;

/// <summary>
/// Answers the searches of every client (web page, Android TV app…): a request "GET /Items?searchTerm=…"
/// for movies, series, episodes or collections is answered with JellySearch's results. Other searches
/// (music, live TV, people, photos) and any failure go to Jellyfin as usual.
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

    public async Task InvokeAsync(HttpContext context, IAuthorizationContext authorizationContext, SearchService search)
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
        try
        {
            outcome = search.Search(auth.User, term);
            var parent = Guid.TryParse(request.Query["parentId"], out var p) && p != Guid.Empty ? p : (Guid?)null;
            ids = search.Visible(auth.User, outcome, asked.Ours, parent);

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

        var start = int.TryParse(request.Query["startIndex"], out var s) ? Math.Max(0, s) : 0;
        var limit = int.TryParse(request.Query["limit"], out var l) && l > 0 ? Math.Min(l, 800) : 800;
        var page = ids.Skip(start).Take(limit).ToList();
        await IdsForwarder.ForwardAsync(context, _next, page, ids.Count, start, outcome.Query.Syntax).ConfigureAwait(false);
    }

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
