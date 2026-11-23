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
/// The saved searches as tiles in "My media" of the Android TV app (which has no web script): each one is
/// one more library, of type "collections", whose grid lists the results of the search, always up to
/// date. Nothing is stored in Jellyfin: the libraries exist only in the answers to the TV app.
/// </summary>
public partial class TvSavedViewsMiddleware
{
    private const string GuidPattern = "[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}";

    private static readonly Dictionary<string, string> ClientSorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DateCreated"] = "ajout",
        ["PremiereDate"] = "annee",
        ["ProductionYear"] = "annee",
        ["CommunityRating"] = "note",
        ["CriticRating"] = "critique",
        ["Runtime"] = "duree",
        ["Random"] = "hasard"
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

    public async Task InvokeAsync(HttpContext context, IAuthorizationContext authorizationContext, IServerApplicationHost appHost, SavedSearchStore store, SearchService search)
    {
        var request = context.Request;
        var path = request.Path.Value ?? string.Empty;
        if (!Plugin.CurrentConfiguration.TvSavedTiles || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // images: without authentication, like Jellyfin's own (they show the name of the tile only)
        var image = ImageRoute().Match(path);
        if (image.Success && Guid.TryParse(image.Groups["id"].Value, out var imageId) && store.Get(imageId) is { } pictured)
        {
            var width = int.TryParse(request.Query["maxWidth"], out var w) ? w : int.TryParse(request.Query["fillWidth"], out var fw) ? fw : 960;
            var png = TileImage.Png(pictured.Name, pictured.Description, width);
            context.Response.ContentType = "image/png";
            context.Response.Headers.CacheControl = "public, max-age=86400";
            context.Response.ContentLength = png.Length;
            if (!HttpMethods.IsHead(request.Method))
            {
                await context.Response.Body.WriteAsync(png, context.RequestAborted).ConfigureAwait(false);
            }

            return;
        }

        var isViews = ViewsRoute().IsMatch(path);
        var item = ItemRoute().Match(path);
        var itemId = item.Success && Guid.TryParse(item.Groups["id"].Value, out var parsedId) ? parsedId : Guid.Empty;
        var parentId = ItemsRoute().IsMatch(path) && Guid.TryParse(request.Query["parentId"], out var parsedParent) ? parsedParent : Guid.Empty;
        var target = itemId != Guid.Empty ? store.Get(itemId) : parentId != Guid.Empty ? store.Get(parentId) : null;
        if (!isViews && target is null)
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

            if (itemId != Guid.Empty)
            {
                await WriteJsonAsync(context, ViewDto(target!, appHost.SystemId)).ConfigureAwait(false);
                return;
            }

            // the grid of the tile: the results of the search, in the order the grid asks for
            var query = target!.Query;
            var sortBy = request.Query["sortBy"].ToString().Split(',')[0];
            if (ClientSorts.TryGetValue(sortBy, out var sort))
            {
                query += " tri:" + sort;
            }
            else if (sortBy.Equals("SortName", StringComparison.OrdinalIgnoreCase) && QueryParser.Parse(query, search.Index).Sort == SortKey.Relevance)
            {
                query += " tri:titre";
            }

            var outcome = search.Search(auth.User, query);
            var kinds = SearchMiddleware.KindsAsked(request.Query)?.Ours is { Count: > 0 } asked ? asked : [DocKind.Movie, DocKind.Series, DocKind.BoxSet, DocKind.Episode];
            var ids = search.Visible(auth.User, outcome, kinds, null);
            var start = int.TryParse(request.Query["startIndex"], out var s) ? Math.Max(0, s) : 0;
            var limit = int.TryParse(request.Query["limit"], out var l) && l > 0 ? Math.Min(l, 500) : 100;
            await IdsForwarder.ForwardAsync(context, _next, ids.Skip(start).Take(limit).ToList(), ids.Count, start, outcome.Query.Syntax).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !context.Response.HasStarted)
        {
            _logger.LogWarning(ex, "JellySearch: tuile de recherche non servie pour {Path}", path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private async Task AddViewsAsync(HttpContext context, IReadOnlyList<SavedSearch> saved, string serverId)
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

        var bytes = buffer.ToArray();
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
        await original.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
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
