using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.JellySearch.Media;
using Jellyfin.Plugin.JellySearch.Search;
using Jellyfin.Plugin.JellySearch.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySearch.Saved;

/// <summary>
/// The grids of the Android TV app (which has no web script):
/// - the saved searches, as tiles in "My media": each one is one more library, of type "collections";
/// - the card "N résultats" at the start of a search (see <see cref="SearchMiddleware"/>).
/// Their grid lists the results of the search, always up to date, vertically and by IMDb rating unless the
/// user chose otherwise in the app. Nothing is stored in Jellyfin: these libraries exist only in the
/// answers to the TV app.
/// </summary>
public partial class TvSavedViewsMiddleware
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}";

    // the orders of the grid of the TV app, as "tri:" names (the community rating is IMDb's when known)
    private static readonly Dictionary<string, string> ClientSorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SortName"] = "titre",
        ["DateCreated"] = "ajout",
        ["PremiereDate"] = "annee",
        ["ProductionYear"] = "annee",
        ["CriticRating"] = "critique",
        ["Runtime"] = "duree",
        ["Random"] = "hasard"
    };

    // the order the grid starts with, for the order of a query (names of the TV app's preferences)
    private static readonly Dictionary<SortKey, string> DefaultClientSorts = new()
    {
        [SortKey.Title] = "SORT_NAME",
        [SortKey.Added] = "DATE_CREATED",
        [SortKey.YearNewest] = "PREMIERE_DATE",
        [SortKey.Critic] = "CRITIC_RATING"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<TvSavedViewsMiddleware> _logger;

    public TvSavedViewsMiddleware(RequestDelegate next, ILogger<TvSavedViewsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    [GeneratedRegex("/(?:UserViews|Users/" + GuidPattern + "/Views)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViewsRoute();

    [GeneratedRegex("/(?:Users/" + GuidPattern + "/)?Items/(?<id>" + GuidPattern + ")/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemRoute();

    [GeneratedRegex("/(?:Users/" + GuidPattern + "/)?Items/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemsRoute();

    [GeneratedRegex("/Items/(?<id>" + GuidPattern + ")/Images/(?<type>[A-Za-z]+)(?:/\\d+)?/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageRoute();

    [GeneratedRegex("/DisplayPreferences/(?<id>[^/]+)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreferencesRoute();

    /// <summary>A grid of the TV app made by JellySearch: a saved search or the results of a search.</summary>
    private sealed record Grid(Guid UserId, string Query, Func<string, BaseItemDto> Dto, Func<int, byte[]> Image);

    public async Task InvokeAsync(HttpContext context, IAuthorizationContext authorizationContext, IServerApplicationHost appHost, SavedSearchStore store, ResultViews views, SearchService search)
    {
        var request = context.Request;
        var path = request.Path.Value ?? string.Empty;
        var config = Plugin.CurrentConfiguration;
        if (!(config.TvSavedTiles || config.TvResultsCard) || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        Grid? Find(Guid id)
        {
            if (config.TvSavedTiles && store.Get(id) is { } saved)
            {
                return new Grid(saved.UserId, saved.Query, serverId => ViewDto(saved, serverId), width => TileImage.Png(saved.Name, saved.Description, width));
            }

            if (config.TvResultsCard && views.Get(id) is { } view)
            {
                return new Grid(view.UserId, view.Query, serverId => SearchMiddleware.CardDto(view, serverId), width => TileImage.ResultsPng(view.Count, view.Description, view.SortLabel, width));
            }

            return null;
        }

        // images: without authentication, like Jellyfin's own (they show the name and the filters only)
        var image = ImageRoute().Match(path);
        if (image.Success && Guid.TryParse(image.Groups["id"].Value, out var imageId) && Find(imageId) is { } pictured)
        {
            var width = ImageWidth(request.Query);
            var png = pictured.Image(width);
            context.Response.ContentType = "image/png";
            context.Response.Headers.CacheControl = "public, max-age=86400";
            context.Response.ContentLength = png.Length;
            if (!HttpMethods.IsHead(request.Method))
            {
                await context.Response.Body.WriteAsync(png, context.RequestAborted).ConfigureAwait(false);
            }

            return;
        }

        var isViews = config.TvSavedTiles && ViewsRoute().IsMatch(path);
        var preferences = PreferencesRoute().Match(path);
        var preferencesId = preferences.Success ? Uri.UnescapeDataString(preferences.Groups["id"].Value) : null;
        Grid? preferencesOf = null;
        if (preferencesId is not null && !preferencesId.Equals(ResultViews.DisplayPreferencesId, StringComparison.OrdinalIgnoreCase))
        {
            preferencesOf = Guid.TryParse(preferencesId, out var pid) ? Find(pid) : null;
            if (preferencesOf is null)
            {
                preferencesId = null;
            }
        }

        var item = ItemRoute().Match(path);
        var itemId = item.Success && Guid.TryParse(item.Groups["id"].Value, out var parsedId) ? parsedId : Guid.Empty;
        var parentId = ItemsRoute().IsMatch(path) && Guid.TryParse(request.Query["parentId"], out var parsedParent) ? parsedParent : Guid.Empty;
        var target = itemId != Guid.Empty ? Find(itemId) : parentId != Guid.Empty ? Find(parentId) : null;
        if (!isViews && preferencesId is null && target is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var auth = await authorizationContext.GetAuthorizationInfo(context).ConfigureAwait(false);
        if (!auth.IsAuthenticated || auth.User is null || ClientClassifier.Classify(auth.Client) != ClientKind.AndroidTv
            || (target is not null && target.UserId != auth.User.Id))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        try
        {
            if (isViews)
            {
                await AddViewsAsync(context, store.ForUser(auth.User.Id), appHost.SystemId).ConfigureAwait(false);
                return;
            }

            if (preferencesId is not null)
            {
                var querySort = preferencesOf is null ? SortKey.Relevance : QueryParser.Parse(preferencesOf.Query, search.Index).Sort;
                await DefaultPreferencesAsync(context, DefaultClientSort(querySort)).ConfigureAwait(false);
                return;
            }

            if (itemId != Guid.Empty)
            {
                await WriteJsonAsync(context, target!.Dto(appHost.SystemId)).ConfigureAwait(false);
                return;
            }

            // the grid: the results of the search, in the order and with the filters the grid asks for
            var query = GridQuery(target!.Query, request.Query, search);
            var outcome = search.Search(auth.User, query);
            var kinds = SearchMiddleware.KindsAsked(request.Query)?.Ours is { Count: > 0 } asked ? asked : [DocKind.Movie, DocKind.Series, DocKind.BoxSet, DocKind.Episode];
            var ids = search.Visible(auth.User, outcome, kinds, null);
            var start = int.TryParse(request.Query["startIndex"], out var s) ? Math.Max(0, s) : 0;
            var limit = int.TryParse(request.Query["limit"], out var l) && l > 0 ? Math.Min(l, 500) : 100;
            await IdsForwarder.ForwardAsync(context, _next, ids.Skip(start).Take(limit).ToList(), ids.Count, start, outcome.Query.Syntax).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !context.Response.HasStarted)
        {
            _logger.LogWarning(ex, "JellySearch: grille de recherche non servie pour {Path}", path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    /// <summary>
    /// The query of a grid: the search, then the order chosen in the grid (unless it is the one the grid
    /// starts with and the search has its own) and the filters of the grid (unwatched, favourites).
    /// </summary>
    internal static string GridQuery(string query, IQueryCollection parameters, SearchService search)
    {
        var querySort = QueryParser.Parse(query, search.Index).Sort;
        var sortBy = parameters["sortBy"].ToString().Split(',')[0].Trim();
        var startsWith = DefaultClientSort(querySort);
        var asked = sortBy.Equals("CommunityRating", StringComparison.OrdinalIgnoreCase) ? "COMMUNITY_RATING" : DefaultClientSorts.Values.FirstOrDefault(v => v.Replace("_", string.Empty, StringComparison.Ordinal).Equals(sortBy, StringComparison.OrdinalIgnoreCase));
        var keep = querySort != SortKey.Relevance && asked == startsWith;
        if (!keep)
        {
            if (sortBy.Equals("CommunityRating", StringComparison.OrdinalIgnoreCase))
            {
                query += search.Ratings.Count > 0 ? " tri:imdb" : " tri:note";
            }
            else if (ClientSorts.TryGetValue(sortBy, out var sort))
            {
                query += " tri:" + sort;
            }
        }

        var filters = IdsForwarder.List(parameters, "filters");
        if (filters.Contains("IsUnplayed"))
        {
            query += " vu:non";
        }
        else if (filters.Contains("IsPlayed"))
        {
            query += " vu:oui";
        }

        if (filters.Contains("IsFavorite"))
        {
            query += " favori:oui";
        }

        return query;
    }

    /// <summary>The order a grid starts with: the order of its search when the app has it, else the rating.</summary>
    internal static string DefaultClientSort(SortKey querySort)
        => DefaultClientSorts.TryGetValue(querySort, out var name) ? name : "COMMUNITY_RATING";

    // the images are 16:9
    private static int ImageWidth(IQueryCollection query)
    {
        static int? Int(IQueryCollection q, string name) => int.TryParse(q[name], out var v) && v > 0 ? v : null;
        if ((Int(query, "maxWidth") ?? Int(query, "fillWidth") ?? Int(query, "width")) is { } w)
        {
            return w;
        }

        if ((Int(query, "maxHeight") ?? Int(query, "fillHeight") ?? Int(query, "height")) is { } h)
        {
            return (h * 16) / 9;
        }

        return 960;
    }

    // the grid starts vertical (rows of posters, scrolled down) and sorted, until the user changes it in the app
    private async Task DefaultPreferencesAsync(HttpContext context, string sortBy)
    {
        var bytes = await CaptureAsync(context).ConfigureAwait(false);
        if (context.Response.StatusCode == StatusCodes.Status200OK && bytes.Length > 0 && JsonNode.Parse(bytes) is JsonObject prefs)
        {
            if (prefs["CustomPrefs"] is not JsonObject custom)
            {
                custom = new JsonObject();
                prefs["CustomPrefs"] = custom;
            }

            if (custom["SortBy"] is null)
            {
                custom["SortBy"] = sortBy;
                custom["SortOrder"] = sortBy is "SORT_NAME" ? "ASCENDING" : "DESCENDING";
            }

            custom["GridDirection"] ??= "VERTICAL";
            bytes = Encoding.UTF8.GetBytes(prefs.ToJsonString());
        }

        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    // runs the rest of the pipeline and returns its answer, not yet sent
    private async Task<byte[]> CaptureAsync(HttpContext context)
    {
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("If-None-Match");
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        return buffer.ToArray();
    }

    private async Task AddViewsAsync(HttpContext context, IReadOnlyList<SavedSearch> saved, string serverId)
    {
        var bytes = await CaptureAsync(context).ConfigureAwait(false);
        if (saved.Count > 0 && context.Response.StatusCode == StatusCodes.Status200OK
            && JsonNode.Parse(bytes) is JsonObject result && result["Items"] is JsonArray items)
        {
            foreach (var s in saved)
            {
                items.Add(JsonNode.Parse(JsonSerializer.Serialize(ViewDto(s, serverId), JsonDefaults.Options)));
            }

            if (result["TotalRecordCount"] is not null)
            {
                result["TotalRecordCount"] = items.Count;
            }

            bytes = Encoding.UTF8.GetBytes(result.ToJsonString());
        }

        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    internal static BaseItemDto ViewDto(SavedSearch saved, string serverId) => new()
    {
        Name = saved.Name,
        ServerId = serverId,
        Id = saved.Id,
        Type = BaseItemKind.CollectionFolder,
        CollectionType = CollectionType.boxsets,
        IsFolder = true,
        Overview = saved.Description,
        DisplayPreferencesId = saved.Id.ToString("N"),
        PrimaryImageAspectRatio = 16d / 9d,
        ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = ImageTag(saved) },
        BackdropImageTags = [],
        LocationType = LocationType.Virtual
    };

    // changes with the name, the filters and the version: the TV app downloads the new image
    private static string ImageTag(SavedSearch saved)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(saved.Name + "|" + saved.Description + "|" + typeof(TvSavedViewsMiddleware).Assembly.GetName().Version))).ToLowerInvariant();

    private static async Task WriteJsonAsync<T>(HttpContext context, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
